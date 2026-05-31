use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use anyhow::{Context, Result};
use chrono::{SecondsFormat, Utc};
use clap::Parser;
use redis::AsyncCommands;
use rmux_sdk::{EnsureSession, Rmux};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::process::Command;
use tokio::sync::Semaphore;
use tracing::{error, info, warn};

const JOB_QUEUE: &str = "CODING_AGENT_JOBS";
const REPORT_QUEUE: &str = "CODING_AGENT_REPORTS";
const ACTIVE_JOB_SET: &str = "CODING_AGENT_ACTIVE_JOBS";

#[derive(Parser, Debug, Clone)]
struct Args {
    #[arg(long, default_value = "127.0.0.1:6379")]
    redis: String,

    #[arg(long)]
    work_dir: PathBuf,

    #[arg(long, default_value = "pi")]
    pi_command: String,

    #[arg(long, default_value_t = 2)]
    max_concurrent: usize,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct CodingAgentJobRequest {
    job_id: String,
    chat_id: i64,
    user_id: i64,
    message_id: i64,
    prompt: String,
    working_directory: String,
    agents_json: String,
    timeout_minutes: i32,
    provider: String,
    model: String,
    tools: String,
    created_at_utc: String,
}

#[derive(Debug, Clone, Copy, Serialize)]
#[allow(dead_code)]
enum CodingAgentJobStatus {
    Pending,
    Running,
    Completed,
    Failed,
    Cancelling,
    Cancelled,
    TimedOut,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "PascalCase")]
struct CodingAgentJobReport {
    job_id: String,
    status: CodingAgentJobStatus,
    chat_id: i64,
    user_id: i64,
    message_id: i64,
    prompt: String,
    working_directory: String,
    summary: String,
    output: String,
    error_message: String,
    log_path: String,
    rmux_session_names: Vec<String>,
    started_at_utc: String,
    completed_at_utc: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct CodingAgentControlCommand {
    #[allow(dead_code)]
    job_id: String,
    action: String,
    #[allow(dead_code)]
    reason: String,
}

#[derive(Debug, Deserialize)]
struct AgentSpec {
    name: Option<String>,
    prompt: Option<String>,
    role: Option<String>,
}

#[derive(Debug, Clone)]
struct AgentRun {
    name: String,
    prompt: String,
}

#[derive(Debug)]
struct AgentRunResult {
    status: CodingAgentJobStatus,
    text: String,
    error: String,
    rmux_session_name: Option<String>,
}

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(tracing_subscriber::EnvFilter::from_default_env())
        .init();

    let args = Args::parse();
    let max_concurrent = args.max_concurrent.max(1);
    tokio::fs::create_dir_all(&args.work_dir)
        .await
        .with_context(|| format!("failed to create work dir {}", args.work_dir.display()))?;

    let redis_url = format!("redis://{}/", args.redis);
    let client = redis::Client::open(redis_url)?;
    let semaphore = Arc::new(Semaphore::new(max_concurrent));
    let config = Arc::new(args);

    info!("rmux/pi sidecar started; max_concurrent={max_concurrent}");
    let mut queue_conn = client.get_multiplexed_async_connection().await?;
    loop {
        let result: Option<(String, String)> = redis::cmd("BRPOP")
            .arg(JOB_QUEUE)
            .arg(2)
            .query_async(&mut queue_conn)
            .await?;

        let Some((_, payload)) = result else {
            continue;
        };

        let request = match serde_json::from_str::<CodingAgentJobRequest>(&payload) {
            Ok(request) => request,
            Err(err) => {
                warn!(error = %err, "failed to deserialize coding agent job");
                continue;
            }
        };

        let permit = semaphore.clone().acquire_owned().await?;
        let client_for_job = client.clone();
        let config_for_job = config.clone();
        tokio::spawn(async move {
            let _permit = permit;
            if let Err(err) = process_job(client_for_job, config_for_job, request).await {
                error!(error = %err, "coding agent job processing failed");
            }
        });
    }
}

async fn process_job(
    client: redis::Client,
    config: Arc<Args>,
    request: CodingAgentJobRequest,
) -> Result<()> {
    let started_at = utc_now();
    let log_dir = config.work_dir.join("CodingAgent").join("logs");
    let session_dir = config.work_dir.join("CodingAgent").join("pi_sessions");
    tokio::fs::create_dir_all(&log_dir).await?;
    tokio::fs::create_dir_all(&session_dir).await?;
    let log_path = log_dir.join(format!("{}.log", request.job_id));
    append_line(
        &log_path,
        &format!("job {} started at {}", request.job_id, started_at),
    )
    .await?;
    append_line(
        &log_path,
        &format!(
            "chat={} user={} message={} workspace={} created_at={}",
            request.chat_id,
            request.user_id,
            request.message_id,
            request.working_directory,
            request.created_at_utc
        ),
    )
    .await?;

    let mut conn = client.get_multiplexed_async_connection().await?;
    update_state(
        &mut conn,
        &request.job_id,
        CodingAgentJobStatus::Running,
        &[
            ("startedAtUtc", started_at.as_str()),
            ("logPath", &log_path.to_string_lossy()),
            ("updatedAtUtc", &utc_now()),
        ],
    )
    .await?;

    let mut rmux_session_names = Vec::new();
    let mut outputs = Vec::new();
    let mut final_status = CodingAgentJobStatus::Completed;
    let mut error_message = String::new();
    let agents = parse_agents(&request);

    for agent in agents {
        if is_cancel_requested(&mut conn, &request.job_id).await? {
            final_status = CodingAgentJobStatus::Cancelled;
            error_message = "Job was cancelled before the next agent started.".to_owned();
            break;
        }

        update_state(
            &mut conn,
            &request.job_id,
            CodingAgentJobStatus::Running,
            &[
                ("currentAgent", agent.name.as_str()),
                ("updatedAtUtc", &utc_now()),
            ],
        )
        .await?;

        let result = run_agent(
            client.clone(),
            config.clone(),
            &request,
            &agent,
            &log_path,
            &session_dir,
        )
        .await;

        match result {
            Ok(agent_result) => {
                if let Some(session_name) = agent_result.rmux_session_name {
                    rmux_session_names.push(session_name);
                }
                outputs.push(format!("## {}\n{}", agent.name, agent_result.text));
                match agent_result.status {
                    CodingAgentJobStatus::Completed => {}
                    status => {
                        final_status = status;
                        error_message = agent_result.error;
                        break;
                    }
                }
            }
            Err(err) => {
                final_status = CodingAgentJobStatus::Failed;
                error_message = err.to_string();
                outputs.push(format!("## {}\nError: {}", agent.name, error_message));
                break;
            }
        }
    }

    let completed_at = utc_now();
    let output = outputs.join("\n\n");
    let summary = build_summary(final_status, &output, &error_message);
    let report = CodingAgentJobReport {
        job_id: request.job_id.clone(),
        status: final_status,
        chat_id: request.chat_id,
        user_id: request.user_id,
        message_id: request.message_id,
        prompt: request.prompt.clone(),
        working_directory: request.working_directory.clone(),
        summary: summary.clone(),
        output,
        error_message: error_message.clone(),
        log_path: log_path.to_string_lossy().to_string(),
        rmux_session_names: rmux_session_names.clone(),
        started_at_utc: started_at,
        completed_at_utc: completed_at.clone(),
    };

    update_state(
        &mut conn,
        &request.job_id,
        final_status,
        &[
            ("summary", summary.as_str()),
            ("error", error_message.as_str()),
            ("completedAtUtc", completed_at.as_str()),
            (
                "rmuxSessionNames",
                &serde_json::to_string(&rmux_session_names)?,
            ),
            ("updatedAtUtc", &utc_now()),
        ],
    )
    .await?;

    let report_payload = serde_json::to_string(&report)?;
    let _: usize = conn.lpush(REPORT_QUEUE, report_payload).await?;
    cleanup_job(&mut conn, &request.job_id).await?;
    append_line(
        &log_path,
        &format!("job {} finished with {:?}", request.job_id, final_status),
    )
    .await?;
    Ok(())
}

async fn run_agent(
    client: redis::Client,
    config: Arc<Args>,
    request: &CodingAgentJobRequest,
    agent: &AgentRun,
    log_path: &Path,
    session_dir: &Path,
) -> Result<AgentRunResult> {
    append_line(log_path, &format!("agent {} starting", agent.name)).await?;
    let rmux_session_name = match ensure_rmux_observer(
        &request.job_id,
        &agent.name,
        &request.working_directory,
        log_path,
    )
    .await
    {
        Ok(session_name) => Some(session_name),
        Err(err) => {
            append_line(log_path, &format!("rmux observer failed: {err}")).await?;
            None
        }
    };

    let mut command = Command::new(&config.pi_command);
    command
        .arg("--mode")
        .arg("rpc")
        .args(build_pi_args(request, session_dir, &agent.name))
        .current_dir(&request.working_directory)
        .stdin(std::process::Stdio::piped())
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::piped());

    let mut child = command
        .spawn()
        .with_context(|| format!("failed to spawn pi command '{}'", config.pi_command))?;
    let mut stdin = child.stdin.take().context("pi stdin unavailable")?;
    let stdout = child.stdout.take().context("pi stdout unavailable")?;
    let stderr = child.stderr.take().context("pi stderr unavailable")?;
    let stderr_log_path = log_path.to_path_buf();
    let stderr_task = tokio::spawn(async move {
        let mut lines = BufReader::new(stderr).lines();
        while let Some(line) = lines.next_line().await? {
            append_line(&stderr_log_path, &format!("[pi:stderr] {line}")).await?;
        }
        Result::<()>::Ok(())
    });

    let prompt_command = json!({
        "id": format!("{}-{}", request.job_id, agent.name),
        "type": "prompt",
        "message": agent.prompt,
    });
    stdin
        .write_all(format!("{}\n", prompt_command).as_bytes())
        .await?;
    stdin.flush().await?;

    let mut conn = client.get_multiplexed_async_connection().await?;
    let mut lines = BufReader::new(stdout).lines();
    let timeout_minutes = request.timeout_minutes.max(1) as u64;
    let timeout = tokio::time::sleep(Duration::from_secs(timeout_minutes * 60));
    tokio::pin!(timeout);
    let mut control_interval = tokio::time::interval(Duration::from_secs(2));
    let mut assistant_text = String::new();
    let mut status = CodingAgentJobStatus::Completed;
    let mut error = String::new();
    let mut saw_agent_end = false;

    loop {
        tokio::select! {
            _ = &mut timeout => {
                status = CodingAgentJobStatus::TimedOut;
                error = format!("pi agent timed out after {timeout_minutes} minutes");
                let _ = child.start_kill();
                break;
            }
            _ = control_interval.tick() => {
                if is_cancel_requested(&mut conn, &request.job_id).await? {
                    status = CodingAgentJobStatus::Cancelled;
                    error = "cancel requested".to_owned();
                    let _ = child.start_kill();
                    break;
                }
            }
            line = lines.next_line() => {
                let Some(line) = line? else {
                    break;
                };
                append_line(log_path, &format!("[pi:stdout:{}] {}", agent.name, line)).await?;
                match serde_json::from_str::<Value>(&line) {
                    Ok(value) => {
                        if value.get("type").and_then(Value::as_str) == Some("response") &&
                           value.get("success").and_then(Value::as_bool) == Some(false) {
                            status = CodingAgentJobStatus::Failed;
                            error = value.get("error").and_then(Value::as_str).unwrap_or("pi command failed").to_owned();
                            break;
                        }

                        if value.get("type").and_then(Value::as_str) == Some("message_update") {
                            if let Some(delta) = value
                                .get("assistantMessageEvent")
                                .and_then(|event| event.get("delta"))
                                .and_then(Value::as_str) {
                                assistant_text.push_str(delta);
                            }
                        }

                        if value.get("type").and_then(Value::as_str) == Some("agent_end") {
                            saw_agent_end = true;
                            break;
                        }
                    }
                    Err(err) => {
                        append_line(log_path, &format!("failed to parse pi JSONL: {err}")).await?;
                    }
                }
            }
        }
    }

    if saw_agent_end {
        if let Ok(Some(last_text)) = get_last_assistant_text(&mut stdin, &mut lines, log_path).await
        {
            if !last_text.trim().is_empty() {
                assistant_text = last_text;
            }
        }
    }

    let _ = child.start_kill();
    let exit_status = child.wait().await.ok();
    let _ = stderr_task.await;
    if matches!(status, CodingAgentJobStatus::Completed) {
        if let Some(exit_status) = exit_status {
            if !exit_status.success() && !saw_agent_end {
                status = CodingAgentJobStatus::Failed;
                error = format!("pi exited with status {exit_status}");
            }
        }
    }

    if assistant_text.trim().is_empty() && !error.is_empty() {
        assistant_text = error.clone();
    }

    append_line(
        log_path,
        &format!("agent {} finished with {:?}", agent.name, status),
    )
    .await?;
    Ok(AgentRunResult {
        status,
        text: assistant_text,
        error,
        rmux_session_name,
    })
}

async fn get_last_assistant_text(
    stdin: &mut tokio::process::ChildStdin,
    lines: &mut tokio::io::Lines<BufReader<tokio::process::ChildStdout>>,
    log_path: &Path,
) -> Result<Option<String>> {
    stdin
        .write_all(b"{\"id\":\"get-last-assistant-text\",\"type\":\"get_last_assistant_text\"}\n")
        .await?;
    stdin.flush().await?;

    let timeout = tokio::time::sleep(Duration::from_secs(5));
    tokio::pin!(timeout);
    loop {
        tokio::select! {
            _ = &mut timeout => return Ok(None),
            line = lines.next_line() => {
                let Some(line) = line? else {
                    return Ok(None);
                };
                append_line(log_path, &format!("[pi:stdout:last] {line}")).await?;
                let value: Value = match serde_json::from_str(&line) {
                    Ok(value) => value,
                    Err(_) => continue,
                };
                if value.get("type").and_then(Value::as_str) == Some("response") &&
                   value.get("command").and_then(Value::as_str) == Some("get_last_assistant_text") {
                    return Ok(value
                        .get("data")
                        .and_then(|data| data.get("text"))
                        .and_then(Value::as_str)
                        .map(ToOwned::to_owned));
                }
            }
        }
    }
}

async fn ensure_rmux_observer(
    job_id: &str,
    agent_name: &str,
    working_directory: &str,
    log_path: &Path,
) -> Result<String> {
    let session_name = sanitize_session_name(&format!("tgsb-ca-{job_id}-{agent_name}"));
    let rmux = Rmux::builder().connect_or_start().await?;
    let argv = tail_log_command(log_path);
    rmux.ensure_session(
        EnsureSession::try_named(&session_name)?
            .create_or_reuse()
            .detached(true)
            .working_directory(working_directory)
            .argv(argv),
    )
    .await?;
    Ok(session_name)
}

fn tail_log_command(log_path: &Path) -> Vec<String> {
    let path = log_path.to_string_lossy();
    if cfg!(windows) {
        let escaped = path.replace('\'', "''");
        vec![
            "powershell.exe".to_owned(),
            "-NoLogo".to_owned(),
            "-NoProfile".to_owned(),
            "-Command".to_owned(),
            format!(
                "Write-Host 'TelegramSearchBot coding agent log: {escaped}'; if (Test-Path -LiteralPath '{escaped}') {{ Get-Content -LiteralPath '{escaped}' -Wait }} else {{ while ($true) {{ Start-Sleep -Seconds 5 }} }}"
            ),
        ]
    } else {
        let escaped = shell_single_quote(&path);
        vec![
            "sh".to_owned(),
            "-lc".to_owned(),
            format!(
                "printf '%s\\n' 'TelegramSearchBot coding agent log: {path}'; touch {escaped}; tail -n +1 -f {escaped}"
            ),
        ]
    }
}

fn build_pi_args(
    request: &CodingAgentJobRequest,
    session_dir: &Path,
    agent_name: &str,
) -> Vec<String> {
    let mut args = Vec::new();
    if !request.provider.trim().is_empty() {
        args.push("--provider".to_owned());
        args.push(request.provider.trim().to_owned());
    }
    if !request.model.trim().is_empty() {
        args.push("--model".to_owned());
        args.push(request.model.trim().to_owned());
    }
    if !request.tools.trim().is_empty() {
        args.push("--tools".to_owned());
        args.push(request.tools.trim().to_owned());
    }

    args.push("--session-dir".to_owned());
    args.push(
        session_dir
            .join(&request.job_id)
            .join(sanitize_session_name(agent_name))
            .to_string_lossy()
            .to_string(),
    );
    args
}

fn parse_agents(request: &CodingAgentJobRequest) -> Vec<AgentRun> {
    let agents_json = request.agents_json.trim();
    if agents_json.is_empty() {
        return vec![AgentRun {
            name: "primary".to_owned(),
            prompt: request.prompt.clone(),
        }];
    }

    if let Ok(specs) = serde_json::from_str::<Vec<AgentSpec>>(agents_json) {
        let agents: Vec<_> = specs
            .into_iter()
            .enumerate()
            .map(|(index, spec)| agent_from_spec(request, index, spec))
            .collect();
        if !agents.is_empty() {
            return agents;
        }
    }

    if let Ok(spec) = serde_json::from_str::<AgentSpec>(agents_json) {
        return vec![agent_from_spec(request, 0, spec)];
    }

    vec![AgentRun {
        name: "primary".to_owned(),
        prompt: format!(
            "{}\n\nMulti-agent configuration supplied by caller:\n{}",
            request.prompt, request.agents_json
        ),
    }]
}

fn agent_from_spec(request: &CodingAgentJobRequest, index: usize, spec: AgentSpec) -> AgentRun {
    let name = spec
        .name
        .filter(|value| !value.trim().is_empty())
        .unwrap_or_else(|| format!("agent-{}", index + 1));
    let prompt = match (spec.prompt, spec.role) {
        (Some(prompt), _) if !prompt.trim().is_empty() => format!(
            "Original task:\n{}\n\nAgent {} task:\n{}",
            request.prompt,
            name,
            prompt.trim()
        ),
        (_, Some(role)) if !role.trim().is_empty() => format!(
            "Original task:\n{}\n\nYou are agent {}. Role:\n{}",
            request.prompt,
            name,
            role.trim()
        ),
        _ => request.prompt.clone(),
    };

    AgentRun {
        name: sanitize_session_name(&name),
        prompt,
    }
}

async fn update_state(
    conn: &mut redis::aio::MultiplexedConnection,
    job_id: &str,
    status: CodingAgentJobStatus,
    entries: &[(&str, &str)],
) -> Result<()> {
    let key = job_state_key(job_id);
    let mut values = vec![
        ("status", format!("{:?}", status)),
        ("updatedAtUtc", utc_now()),
    ];
    values.extend(
        entries
            .iter()
            .map(|(key, value)| (*key, (*value).to_owned())),
    );
    let _: () = conn.hset_multiple(key, &values).await?;
    Ok(())
}

async fn cleanup_job(conn: &mut redis::aio::MultiplexedConnection, job_id: &str) -> Result<()> {
    let _: usize = conn.srem(ACTIVE_JOB_SET, job_id).await?;
    let _: usize = conn.del(control_key(job_id)).await?;
    Ok(())
}

async fn is_cancel_requested(
    conn: &mut redis::aio::MultiplexedConnection,
    job_id: &str,
) -> Result<bool> {
    let value: Option<String> = conn.get(control_key(job_id)).await?;
    let Some(value) = value else {
        return Ok(false);
    };

    let command: CodingAgentControlCommand = serde_json::from_str(&value)?;
    Ok(command.action.eq_ignore_ascii_case("cancel"))
}

fn build_summary(status: CodingAgentJobStatus, output: &str, error: &str) -> String {
    let mut summary = format!("Status: {:?}", status);
    if !error.trim().is_empty() {
        summary.push_str("\nError: ");
        summary.push_str(error.trim());
    }
    if !output.trim().is_empty() {
        summary.push_str("\n\n");
        summary.push_str(trim_chars(output.trim(), 4000).as_str());
    }
    summary
}

async fn append_line(path: &Path, line: &str) -> Result<()> {
    if let Some(parent) = path.parent() {
        tokio::fs::create_dir_all(parent).await?;
    }
    let mut file = tokio::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)
        .await?;
    file.write_all(line.as_bytes()).await?;
    file.write_all(b"\n").await?;
    Ok(())
}

fn job_state_key(job_id: &str) -> String {
    format!("CODING_AGENT_JOB:{job_id}")
}

fn control_key(job_id: &str) -> String {
    format!("CODING_AGENT_CONTROL:{job_id}")
}

fn sanitize_session_name(value: &str) -> String {
    let mut sanitized: String = value
        .chars()
        .map(|ch| {
            if ch.is_ascii_alphanumeric() || ch == '-' || ch == '_' {
                ch
            } else {
                '-'
            }
        })
        .collect();
    while sanitized.contains("--") {
        sanitized = sanitized.replace("--", "-");
    }
    sanitized = sanitized.trim_matches('-').to_owned();
    if sanitized.is_empty() {
        sanitized = "agent".to_owned();
    }
    if sanitized.len() > 80 {
        sanitized.truncate(80);
    }
    sanitized
}

fn shell_single_quote(value: &str) -> String {
    format!("'{}'", value.replace('\'', "'\"'\"'"))
}

fn trim_chars(value: &str, max_chars: usize) -> String {
    if value.chars().count() <= max_chars {
        return value.to_owned();
    }
    let mut output: String = value.chars().take(max_chars).collect();
    output.push_str("\n... [truncated]");
    output
}

fn utc_now() -> String {
    Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn request_with_agents(agents_json: &str) -> CodingAgentJobRequest {
        CodingAgentJobRequest {
            job_id: "ca_test".to_owned(),
            chat_id: 1,
            user_id: 2,
            message_id: 3,
            prompt: "fix the bug".to_owned(),
            working_directory: ".".to_owned(),
            agents_json: agents_json.to_owned(),
            timeout_minutes: 5,
            provider: String::new(),
            model: String::new(),
            tools: String::new(),
            created_at_utc: String::new(),
        }
    }

    #[test]
    fn parse_agents_defaults_to_primary() {
        let agents = parse_agents(&request_with_agents(""));
        assert_eq!(agents.len(), 1);
        assert_eq!(agents[0].name, "primary");
        assert_eq!(agents[0].prompt, "fix the bug");
    }

    #[test]
    fn parse_agents_accepts_json_array() {
        let agents = parse_agents(&request_with_agents(
            r#"[{"name":"implementer","prompt":"make the change"},{"name":"reviewer","role":"review only"}]"#,
        ));
        assert_eq!(agents.len(), 2);
        assert_eq!(agents[0].name, "implementer");
        assert!(agents[0].prompt.contains("make the change"));
        assert_eq!(agents[1].name, "reviewer");
        assert!(agents[1].prompt.contains("review only"));
    }

    #[test]
    fn sanitize_session_name_removes_unsafe_chars() {
        assert_eq!(sanitize_session_name("hello world/../x"), "hello-world-x");
    }

    #[test]
    fn build_pi_args_adds_optional_flags_and_session_dir() {
        let mut request = request_with_agents("");
        request.provider = "openai".to_owned();
        request.model = "gpt-5-codex".to_owned();
        request.tools = "fs,shell".to_owned();
        let args = build_pi_args(&request, Path::new("/tmp/sessions"), "primary");
        assert!(args.windows(2).any(|pair| pair == ["--provider", "openai"]));
        assert!(args
            .windows(2)
            .any(|pair| pair == ["--model", "gpt-5-codex"]));
        assert!(args.windows(2).any(|pair| pair == ["--tools", "fs,shell"]));
        assert!(args.iter().any(|value| value.contains("ca_test")));
    }
}
