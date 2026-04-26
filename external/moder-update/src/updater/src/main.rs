use std::env;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::process::{self, Command};
use std::thread;
use std::time::{Duration, Instant};

use base64::Engine;
use sha2::{Digest, Sha512};
use walkdir::WalkDir;

/// Standalone updater process that replaces application files and restarts the main application.
fn main() {
    let args: Vec<String> = env::args().collect();

    if args.len() <= 1 || args.contains(&"--help".to_string()) || args.contains(&"-h".to_string())
    {
        print_usage();
        process::exit(0);
    }

    match parse_args(&args[1..]) {
        Ok(config) => {
            let code = run_update(&config);
            process::exit(code);
        }
        Err(e) => {
            eprintln!("[Moder.Update.Updater] Fatal error: {e}");
            process::exit(1);
        }
    }
}

/// Core update logic: wait for target process, replace files, restart.
fn run_update(config: &UpdaterConfig) -> i32 {
    println!(
        "[Moder.Update.Updater] Waiting for process {} to exit...",
        config.target_pid
    );

    if !wait_for_process_exit(config.target_pid, config.wait_timeout) {
        eprintln!(
            "[Moder.Update.Updater] Process {} did not exit in time, killing...",
            config.target_pid
        );
        kill_process(config.target_pid);
    }

    println!("[Moder.Update.Updater] Target process exited. Starting file replacement...");

    let mut replaced_files: Vec<String> = Vec::new();

    let target_dir = match Path::new(&config.target_path)
        .parent()
        .map(|p| p.to_path_buf())
    {
        Some(dir) => {
            if dir.as_os_str().is_empty() {
                PathBuf::from(".")
            } else {
                dir
            }
        }
        None => {
            eprintln!("[Moder.Update.Updater] Cannot determine target directory.");
            restart_target(config);
            return 1;
        }
    };

    match replace_files(config, &target_dir, &mut replaced_files) {
        Ok(()) => {
            println!(
                "[Moder.Update.Updater] Replaced {} files.",
                replaced_files.len()
            );
            cleanup_directory(&config.staging_dir);

            if let Some(ref backup_dir) = config.backup_dir {
                commit_backup(backup_dir);
            }
        }
        Err(e) => {
            eprintln!(
                "[Moder.Update.Updater] Error during file replacement: {e}"
            );

            if let Some(ref backup_dir) = config.backup_dir {
                if !replaced_files.is_empty() {
                    println!("[Moder.Update.Updater] Attempting rollback...");
                    match rollback(&replaced_files, backup_dir, &target_dir) {
                        Ok(()) => {
                            println!("[Moder.Update.Updater] Rollback completed.")
                        }
                        Err(re) => {
                            eprintln!("[Moder.Update.Updater] Rollback failed: {re}")
                        }
                    }
                }
            }

            restart_target(config);
            return 1;
        }
    }

    restart_target(config);
    println!("[Moder.Update.Updater] Update complete.");
    0
}

/// Replace all files from staging directory to target directory.
fn replace_files(
    config: &UpdaterConfig,
    target_dir: &Path,
    replaced_files: &mut Vec<String>,
) -> io::Result<()> {
    for entry in WalkDir::new(&config.staging_dir)
        .into_iter()
        .filter_map(|e| e.ok())
        .filter(|e| e.file_type().is_file())
    {
        let staging_path = entry.path();
        let relative_path = staging_path
            .strip_prefix(&config.staging_dir)
            .map_err(|e| io::Error::other(e.to_string()))?;

        let target_path = target_dir.join(relative_path);
        let relative_str = relative_path.to_string_lossy().to_string();

        println!("  Replacing: {relative_str}");

        // Create backup if backup directory is specified and target file exists
        if let Some(ref backup_dir) = config.backup_dir {
            if target_path.exists() {
                let backup_path = Path::new(backup_dir).join(relative_path);
                if let Some(parent) = backup_path.parent() {
                    fs::create_dir_all(parent)?;
                }
                fs::copy(&target_path, &backup_path)?;
            }
        }

        // Ensure target directory exists
        if let Some(parent) = target_path.parent() {
            fs::create_dir_all(parent)?;
        }

        // Replace the file
        replace_file(&target_path, staging_path)?;

        // Verify hash of the replaced file matches the source
        let source_hash = compute_sha512(staging_path)?;
        let target_hash = compute_sha512(&target_path)?;
        if source_hash != target_hash {
            return Err(io::Error::new(
                io::ErrorKind::InvalidData,
                format!(
                    "Hash mismatch after replacing '{}': expected {}, got {}",
                    relative_str, source_hash, target_hash
                ),
            ));
        }

        replaced_files.push(relative_str);
    }

    Ok(())
}

/// Replace a single file at the target path with the staging file.
fn replace_file(target_path: &Path, staging_path: &Path) -> io::Result<()> {
    if target_path.exists() {
        fs::remove_file(target_path)?;
    }
    fs::copy(staging_path, target_path)?;
    Ok(())
}

/// Compute SHA512 hash of a file, returned as uppercase hex string.
pub fn compute_sha512(path: &Path) -> io::Result<String> {
    let data = fs::read(path)?;
    Ok(compute_sha512_bytes(&data))
}

/// Compute SHA512 hash of a byte slice, returned as uppercase hex string.
pub fn compute_sha512_bytes(data: &[u8]) -> String {
    let mut hasher = Sha512::new();
    hasher.update(data);
    let result = hasher.finalize();
    result
        .iter()
        .map(|b| format!("{b:02X}"))
        .collect::<String>()
}

/// Verify that a file's SHA512 hash matches the expected value.
pub fn verify_file_hash(path: &Path, expected_hash: &str) -> io::Result<bool> {
    let actual_hash = compute_sha512(path)?;
    Ok(actual_hash.eq_ignore_ascii_case(expected_hash))
}

/// Rollback replaced files from backup directory.
fn rollback(
    replaced_files: &[String],
    backup_dir: &str,
    target_dir: &Path,
) -> io::Result<()> {
    for relative_path in replaced_files {
        let backup_path = Path::new(backup_dir).join(relative_path);
        let target_path = target_dir.join(relative_path);

        if backup_path.exists() {
            if let Some(parent) = target_path.parent() {
                fs::create_dir_all(parent)?;
            }
            fs::copy(&backup_path, &target_path)?;
        }
    }
    Ok(())
}

/// Delete backup directory after successful update.
fn commit_backup(backup_dir: &str) {
    let _ = fs::remove_dir_all(backup_dir);
}

/// Wait for a process to exit with a timeout. Returns true if the process exited.
fn wait_for_process_exit(pid: u32, timeout: Duration) -> bool {
    let start = Instant::now();
    while start.elapsed() < timeout {
        if !is_process_running(pid) {
            return true;
        }
        thread::sleep(Duration::from_millis(200));
    }
    !is_process_running(pid)
}

/// Check if a process with the given PID is still running.
#[cfg(windows)]
fn is_process_running(pid: u32) -> bool {
    use std::ptr;

    // PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;

    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn OpenProcess(dwDesiredAccess: u32, bInheritHandle: i32, dwProcessId: u32) -> *mut std::ffi::c_void;
        fn CloseHandle(hObject: *mut std::ffi::c_void) -> i32;
        fn GetExitCodeProcess(hProcess: *mut std::ffi::c_void, lpExitCode: *mut u32) -> i32;
    }

    // STILL_ACTIVE = 259
    const STILL_ACTIVE: u32 = 259;

    unsafe {
        let handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if handle.is_null() || handle == ptr::null_mut() {
            return false;
        }
        let mut exit_code: u32 = 0;
        let result = GetExitCodeProcess(handle, &mut exit_code);
        CloseHandle(handle);
        result != 0 && exit_code == STILL_ACTIVE
    }
}

#[cfg(not(windows))]
fn is_process_running(pid: u32) -> bool {
    // On Unix, check if we can send signal 0 to the process
    Path::new(&format!("/proc/{pid}")).exists()
}

/// Kill a process by PID.
#[cfg(windows)]
fn kill_process(pid: u32) {
    // Use taskkill to kill the process tree
    let _ = Command::new("taskkill")
        .args(["/PID", &pid.to_string(), "/T", "/F"])
        .output();
    // Wait briefly for process to terminate
    thread::sleep(Duration::from_secs(2));
}

#[cfg(not(windows))]
fn kill_process(pid: u32) {
    let _ = Command::new("kill")
        .args(["-9", &pid.to_string()])
        .output();
    thread::sleep(Duration::from_secs(2));
}

/// Restart the target application.
fn restart_target(config: &UpdaterConfig) {
    println!(
        "[Moder.Update.Updater] Restarting: {}",
        config.target_path
    );

    let mut cmd = Command::new(&config.target_path);
    if let Some(ref args) = config.restart_args {
        cmd.args(args);
    }

    let _ = cmd.spawn();
}

/// Best-effort cleanup of a directory.
fn cleanup_directory(dir: &str) {
    let _ = fs::remove_dir_all(dir);
}

/// Parse command-line arguments into UpdaterConfig.
fn parse_args(args: &[String]) -> Result<UpdaterConfig, String> {
    let mut target_pid: Option<u32> = None;
    let mut target_path: Option<String> = None;
    let mut staging_dir: Option<String> = None;
    let mut backup_dir: Option<String> = None;
    let mut wait_timeout_sec: u64 = 30;
    let mut restart_args: Option<Vec<String>> = None;

    let mut i = 0;
    while i < args.len() {
        match args[i].as_str() {
            "--target-pid" if i + 1 < args.len() => {
                i += 1;
                target_pid = Some(
                    args[i]
                        .parse()
                        .map_err(|e| format!("Invalid --target-pid: {e}"))?,
                );
            }
            "--target-path" if i + 1 < args.len() => {
                i += 1;
                target_path = Some(args[i].clone());
            }
            "--staging-dir" if i + 1 < args.len() => {
                i += 1;
                staging_dir = Some(args[i].clone());
            }
            "--backup-dir" if i + 1 < args.len() => {
                i += 1;
                backup_dir = Some(args[i].clone());
            }
            "--wait-timeout" if i + 1 < args.len() => {
                i += 1;
                wait_timeout_sec = args[i]
                    .parse()
                    .map_err(|e| format!("Invalid --wait-timeout: {e}"))?;
            }
            "--restart-args" if i + 1 < args.len() => {
                i += 1;
                let decoded_bytes = base64::engine::general_purpose::STANDARD
                    .decode(&args[i])
                    .map_err(|e| format!("Invalid --restart-args base64: {e}"))?;
                let decoded = String::from_utf8(decoded_bytes)
                    .map_err(|e| format!("Invalid --restart-args UTF-8: {e}"))?;
                restart_args = Some(
                    decoded
                        .split('\0')
                        .filter(|s| !s.is_empty())
                        .map(|s| s.to_string())
                        .collect(),
                );
            }
            _ => {}
        }
        i += 1;
    }

    Ok(UpdaterConfig {
        target_pid: target_pid.ok_or("--target-pid is required.")?,
        target_path: target_path.ok_or("--target-path is required.")?,
        staging_dir: staging_dir.ok_or("--staging-dir is required.")?,
        backup_dir,
        wait_timeout: Duration::from_secs(wait_timeout_sec),
        restart_args,
    })
}

/// Print usage information.
fn print_usage() {
    println!("Moder.Update.Updater - Dual-process file updater");
    println!();
    println!("Usage:");
    println!(
        "  Moder.Update.Updater --target-pid <pid> --target-path <path> --staging-dir <dir> [options]"
    );
    println!();
    println!("Required:");
    println!("  --target-pid <pid>       PID of the main application process to wait for");
    println!("  --target-path <path>     Path to the main application executable");
    println!("  --staging-dir <dir>      Directory containing new files to replace");
    println!();
    println!("Options:");
    println!(
        "  --backup-dir <dir>       Directory for backing up original files (enables rollback)"
    );
    println!(
        "  --wait-timeout <sec>     Seconds to wait for target process exit (default: 30)"
    );
    println!(
        "  --restart-args <base64>  Base64-encoded restart arguments (null-separated)"
    );
    println!("  --help, -h               Show this help message");
}

/// Configuration parsed from command-line arguments.
#[derive(Debug)]
struct UpdaterConfig {
    target_pid: u32,
    target_path: String,
    staging_dir: String,
    backup_dir: Option<String>,
    wait_timeout: Duration,
    restart_args: Option<Vec<String>>,
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use tempfile::TempDir;

    // ========================================================================
    // SHA512 Hash Computation Tests
    // ========================================================================

    #[test]
    fn test_sha512_known_value() {
        // SHA512 of empty string
        let hash = compute_sha512_bytes(b"");
        assert_eq!(
            hash,
            "CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE\
             47D0D13C5D85F2B0FF8318D2877EEC2F63B931BD47417A81A538327AF927DA3E"
        );
    }

    #[test]
    fn test_sha512_hello_world() {
        let hash = compute_sha512_bytes(b"Hello, World!");
        // Known SHA512 of "Hello, World!"
        assert_eq!(
            hash,
            "374D794A95CDCFD8B35993185FEF9BA368F160D8DAF432D08BA9F1ED1E5ABE6C\
             C69291E0FA2FE0006A52570EF18C19DEF4E617C33CE52EF0A6E5FBE318CB0387"
        );
    }

    #[test]
    fn test_sha512_bytes_deterministic() {
        let data = b"test data for hashing";
        let hash1 = compute_sha512_bytes(data);
        let hash2 = compute_sha512_bytes(data);
        assert_eq!(hash1, hash2, "SHA512 should be deterministic");
    }

    #[test]
    fn test_sha512_different_content_different_hash() {
        let hash1 = compute_sha512_bytes(b"content A");
        let hash2 = compute_sha512_bytes(b"content B");
        assert_ne!(hash1, hash2, "Different content should produce different hashes");
    }

    #[test]
    fn test_sha512_large_data() {
        // Test with 1MB of data
        let data: Vec<u8> = (0..1_000_000).map(|i| (i % 256) as u8).collect();
        let hash1 = compute_sha512_bytes(&data);
        let hash2 = compute_sha512_bytes(&data);
        assert_eq!(hash1, hash2, "SHA512 of large data should be deterministic");
        assert_eq!(hash1.len(), 128, "SHA512 hex string should be 128 chars");
    }

    #[test]
    fn test_sha512_hash_length() {
        let hash = compute_sha512_bytes(b"any content");
        assert_eq!(hash.len(), 128, "SHA512 hex string should be 128 characters");
    }

    #[test]
    fn test_sha512_uppercase_hex() {
        let hash = compute_sha512_bytes(b"test");
        assert!(
            hash.chars().all(|c| c.is_ascii_hexdigit() && (c.is_ascii_digit() || c.is_ascii_uppercase())),
            "SHA512 should be uppercase hex"
        );
    }

    // ========================================================================
    // File Hash Computation Tests
    // ========================================================================

    #[test]
    fn test_compute_sha512_file() {
        let dir = TempDir::new().unwrap();
        let file_path = dir.path().join("test.txt");
        fs::write(&file_path, "Hello, World!").unwrap();

        let hash = compute_sha512(&file_path).unwrap();
        let expected = compute_sha512_bytes(b"Hello, World!");
        assert_eq!(hash, expected);
    }

    #[test]
    fn test_compute_sha512_empty_file() {
        let dir = TempDir::new().unwrap();
        let file_path = dir.path().join("empty.txt");
        fs::write(&file_path, "").unwrap();

        let hash = compute_sha512(&file_path).unwrap();
        let expected = compute_sha512_bytes(b"");
        assert_eq!(hash, expected);
    }

    #[test]
    fn test_compute_sha512_binary_file() {
        let dir = TempDir::new().unwrap();
        let file_path = dir.path().join("binary.bin");
        let data: Vec<u8> = (0..256).map(|i| i as u8).collect();
        fs::write(&file_path, &data).unwrap();

        let hash = compute_sha512(&file_path).unwrap();
        let expected = compute_sha512_bytes(&data);
        assert_eq!(hash, expected);
    }

    #[test]
    fn test_compute_sha512_nonexistent_file() {
        let result = compute_sha512(Path::new("/nonexistent/file.txt"));
        assert!(result.is_err(), "Should return error for nonexistent file");
    }

    // ========================================================================
    // File Hash Verification Tests
    // ========================================================================

    #[test]
    fn test_verify_file_hash_matching() {
        let dir = TempDir::new().unwrap();
        let file_path = dir.path().join("test.txt");
        let content = b"test content for verification";
        fs::write(&file_path, content).unwrap();

        let expected_hash = compute_sha512_bytes(content);
        assert!(verify_file_hash(&file_path, &expected_hash).unwrap());
    }

    #[test]
    fn test_verify_file_hash_mismatched() {
        let dir = TempDir::new().unwrap();
        let file_path = dir.path().join("test.txt");
        fs::write(&file_path, "actual content").unwrap();

        let wrong_hash = compute_sha512_bytes(b"different content");
        assert!(!verify_file_hash(&file_path, &wrong_hash).unwrap());
    }

    #[test]
    fn test_verify_file_hash_case_insensitive() {
        let dir = TempDir::new().unwrap();
        let file_path = dir.path().join("test.txt");
        let content = b"case test";
        fs::write(&file_path, content).unwrap();

        let hash_upper = compute_sha512_bytes(content);
        let hash_lower = hash_upper.to_lowercase();
        assert!(verify_file_hash(&file_path, &hash_lower).unwrap());
    }

    #[test]
    fn test_verify_file_hash_nonexistent_file() {
        let result = verify_file_hash(Path::new("/nonexistent/file.txt"), "abc");
        assert!(result.is_err());
    }

    // ========================================================================
    // File Replacement with Hash Verification Tests
    // ========================================================================

    #[test]
    fn test_replace_file_new_target() {
        let dir = TempDir::new().unwrap();
        let staging_path = dir.path().join("staging.txt");
        let target_path = dir.path().join("target.txt");

        fs::write(&staging_path, "new content").unwrap();

        replace_file(&target_path, &staging_path).unwrap();

        assert!(target_path.exists());
        assert_eq!(fs::read_to_string(&target_path).unwrap(), "new content");
    }

    #[test]
    fn test_replace_file_existing_target() {
        let dir = TempDir::new().unwrap();
        let staging_path = dir.path().join("staging.txt");
        let target_path = dir.path().join("target.txt");

        fs::write(&target_path, "old content").unwrap();
        fs::write(&staging_path, "new content").unwrap();

        replace_file(&target_path, &staging_path).unwrap();

        assert_eq!(fs::read_to_string(&target_path).unwrap(), "new content");
    }

    #[test]
    fn test_replace_file_preserves_hash() {
        let dir = TempDir::new().unwrap();
        let staging_path = dir.path().join("staging.txt");
        let target_path = dir.path().join("target.txt");

        let content = b"content to verify";
        fs::write(&staging_path, content).unwrap();

        replace_file(&target_path, &staging_path).unwrap();

        let expected_hash = compute_sha512_bytes(content);
        assert!(verify_file_hash(&target_path, &expected_hash).unwrap());
    }

    #[test]
    fn test_replace_file_hash_mismatch_detected() {
        let dir = TempDir::new().unwrap();
        let staging_path = dir.path().join("staging.txt");
        let target_path = dir.path().join("target.txt");

        fs::write(&staging_path, "new content").unwrap();
        replace_file(&target_path, &staging_path).unwrap();

        // Verify that the hash of the target matches staging
        let staging_hash = compute_sha512(&staging_path).unwrap();
        let target_hash = compute_sha512(&target_path).unwrap();
        assert_eq!(staging_hash, target_hash);

        // Verify that a wrong hash is detected
        let wrong_hash = compute_sha512_bytes(b"wrong content");
        assert!(!verify_file_hash(&target_path, &wrong_hash).unwrap());
    }

    // ========================================================================
    // Full Update Flow with Hash Verification Tests
    // ========================================================================

    #[test]
    fn test_replace_files_with_hash_verification() {
        let dir = TempDir::new().unwrap();
        let staging_dir = dir.path().join("staging");
        let target_dir = dir.path().join("target");
        fs::create_dir_all(&staging_dir).unwrap();
        fs::create_dir_all(&target_dir).unwrap();

        // Create staging files
        fs::write(staging_dir.join("file1.txt"), "content 1").unwrap();
        fs::write(staging_dir.join("file2.txt"), "content 2").unwrap();

        // Create existing target file with different content
        fs::write(target_dir.join("file1.txt"), "old content").unwrap();

        let config = UpdaterConfig {
            target_pid: 0,
            target_path: target_dir.join("app.exe").to_string_lossy().to_string(),
            staging_dir: staging_dir.to_string_lossy().to_string(),
            backup_dir: None,
            wait_timeout: Duration::from_secs(5),
            restart_args: None,
        };

        let mut replaced = Vec::new();
        replace_files(&config, &target_dir, &mut replaced).unwrap();

        assert_eq!(replaced.len(), 2);

        // Verify hashes match after replacement
        let hash1 = compute_sha512(&target_dir.join("file1.txt")).unwrap();
        let expected1 = compute_sha512_bytes(b"content 1");
        assert_eq!(hash1, expected1);

        let hash2 = compute_sha512(&target_dir.join("file2.txt")).unwrap();
        let expected2 = compute_sha512_bytes(b"content 2");
        assert_eq!(hash2, expected2);
    }

    #[test]
    fn test_replace_files_with_subdirectories() {
        let dir = TempDir::new().unwrap();
        let staging_dir = dir.path().join("staging");
        let target_dir = dir.path().join("target");

        // Create staging files in subdirectories
        fs::create_dir_all(staging_dir.join("sub")).unwrap();
        fs::write(staging_dir.join("root.txt"), "root content").unwrap();
        fs::write(staging_dir.join("sub").join("nested.txt"), "nested content").unwrap();

        fs::create_dir_all(&target_dir).unwrap();

        let config = UpdaterConfig {
            target_pid: 0,
            target_path: target_dir.join("app.exe").to_string_lossy().to_string(),
            staging_dir: staging_dir.to_string_lossy().to_string(),
            backup_dir: None,
            wait_timeout: Duration::from_secs(5),
            restart_args: None,
        };

        let mut replaced = Vec::new();
        replace_files(&config, &target_dir, &mut replaced).unwrap();

        assert_eq!(replaced.len(), 2);

        // Verify hashes
        assert!(verify_file_hash(
            &target_dir.join("root.txt"),
            &compute_sha512_bytes(b"root content")
        )
        .unwrap());
        assert!(verify_file_hash(
            &target_dir.join("sub").join("nested.txt"),
            &compute_sha512_bytes(b"nested content")
        )
        .unwrap());
    }

    #[test]
    fn test_replace_files_with_backup() {
        let dir = TempDir::new().unwrap();
        let staging_dir = dir.path().join("staging");
        let target_dir = dir.path().join("target");
        let backup_dir = dir.path().join("backup");

        fs::create_dir_all(&staging_dir).unwrap();
        fs::create_dir_all(&target_dir).unwrap();

        // Create original target file
        fs::write(target_dir.join("file.txt"), "original").unwrap();

        // Create staging file with new content
        fs::write(staging_dir.join("file.txt"), "updated").unwrap();

        let config = UpdaterConfig {
            target_pid: 0,
            target_path: target_dir.join("app.exe").to_string_lossy().to_string(),
            staging_dir: staging_dir.to_string_lossy().to_string(),
            backup_dir: Some(backup_dir.to_string_lossy().to_string()),
            wait_timeout: Duration::from_secs(5),
            restart_args: None,
        };

        let mut replaced = Vec::new();
        replace_files(&config, &target_dir, &mut replaced).unwrap();

        // Verify target has new content
        assert_eq!(fs::read_to_string(target_dir.join("file.txt")).unwrap(), "updated");

        // Verify backup has original content
        assert_eq!(
            fs::read_to_string(backup_dir.join("file.txt")).unwrap(),
            "original"
        );

        // Verify hashes
        assert!(verify_file_hash(
            &target_dir.join("file.txt"),
            &compute_sha512_bytes(b"updated")
        )
        .unwrap());
        assert!(verify_file_hash(
            &backup_dir.join("file.txt"),
            &compute_sha512_bytes(b"original")
        )
        .unwrap());
    }

    #[test]
    fn test_rollback_restores_files() {
        let dir = TempDir::new().unwrap();
        let target_dir = dir.path().join("target");
        let backup_dir = dir.path().join("backup");

        fs::create_dir_all(&target_dir).unwrap();
        fs::create_dir_all(&backup_dir).unwrap();

        // Simulate: backup has originals, target has replacements
        fs::write(target_dir.join("file.txt"), "new content").unwrap();
        fs::write(backup_dir.join("file.txt"), "original content").unwrap();

        let replaced = vec!["file.txt".to_string()];
        rollback(&replaced, &backup_dir.to_string_lossy(), &target_dir).unwrap();

        assert_eq!(
            fs::read_to_string(target_dir.join("file.txt")).unwrap(),
            "original content"
        );
    }

    // ========================================================================
    // Argument Parsing Tests
    // ========================================================================

    #[test]
    fn test_parse_args_required_only() {
        let args: Vec<String> = vec![
            "--target-pid", "1234",
            "--target-path", "/app/test.exe",
            "--staging-dir", "/tmp/staging",
        ]
        .into_iter()
        .map(String::from)
        .collect();

        let config = parse_args(&args).unwrap();
        assert_eq!(config.target_pid, 1234);
        assert_eq!(config.target_path, "/app/test.exe");
        assert_eq!(config.staging_dir, "/tmp/staging");
        assert!(config.backup_dir.is_none());
        assert_eq!(config.wait_timeout, Duration::from_secs(30));
        assert!(config.restart_args.is_none());
    }

    #[test]
    fn test_parse_args_all_options() {
        let restart = base64::engine::general_purpose::STANDARD
            .encode("--arg1\0--arg2\0value");

        let args: Vec<String> = vec![
            "--target-pid".to_string(), "5678".to_string(),
            "--target-path".to_string(), "C:\\app\\test.exe".to_string(),
            "--staging-dir".to_string(), "C:\\tmp\\staging".to_string(),
            "--backup-dir".to_string(), "C:\\tmp\\backup".to_string(),
            "--wait-timeout".to_string(), "60".to_string(),
            "--restart-args".to_string(), restart,
        ];

        let config = parse_args(&args).unwrap();
        assert_eq!(config.target_pid, 5678);
        assert_eq!(config.target_path, "C:\\app\\test.exe");
        assert_eq!(config.staging_dir, "C:\\tmp\\staging");
        assert_eq!(config.backup_dir.as_deref(), Some("C:\\tmp\\backup"));
        assert_eq!(config.wait_timeout, Duration::from_secs(60));
        assert_eq!(
            config.restart_args.as_ref().unwrap(),
            &vec!["--arg1".to_string(), "--arg2".to_string(), "value".to_string()]
        );
    }

    #[test]
    fn test_parse_args_missing_target_pid() {
        let args: Vec<String> = vec![
            "--target-path", "/app/test.exe",
            "--staging-dir", "/tmp/staging",
        ]
        .into_iter()
        .map(String::from)
        .collect();

        assert!(parse_args(&args).is_err());
    }

    #[test]
    fn test_parse_args_missing_target_path() {
        let args: Vec<String> = vec![
            "--target-pid", "1234",
            "--staging-dir", "/tmp/staging",
        ]
        .into_iter()
        .map(String::from)
        .collect();

        assert!(parse_args(&args).is_err());
    }

    #[test]
    fn test_parse_args_missing_staging_dir() {
        let args: Vec<String> = vec![
            "--target-pid", "1234",
            "--target-path", "/app/test.exe",
        ]
        .into_iter()
        .map(String::from)
        .collect();

        assert!(parse_args(&args).is_err());
    }

    #[test]
    fn test_parse_args_invalid_pid() {
        let args: Vec<String> = vec![
            "--target-pid", "not_a_number",
            "--target-path", "/app/test.exe",
            "--staging-dir", "/tmp/staging",
        ]
        .into_iter()
        .map(String::from)
        .collect();

        assert!(parse_args(&args).is_err());
    }

    // ========================================================================
    // Process Utility Tests
    // ========================================================================

    #[test]
    fn test_is_process_running_nonexistent() {
        // PID 0 is typically not a regular user process, and very high PIDs don't exist
        assert!(!is_process_running(u32::MAX));
    }

    #[test]
    fn test_wait_for_process_exit_already_exited() {
        // Non-existent PID should return immediately
        let result = wait_for_process_exit(u32::MAX, Duration::from_secs(1));
        assert!(result, "Should return true for non-existent process");
    }

    // ========================================================================
    // Cleanup Tests
    // ========================================================================

    #[test]
    fn test_cleanup_directory() {
        let dir = TempDir::new().unwrap();
        let cleanup_dir = dir.path().join("to_clean");
        fs::create_dir_all(&cleanup_dir).unwrap();
        fs::write(cleanup_dir.join("file.txt"), "content").unwrap();

        cleanup_directory(&cleanup_dir.to_string_lossy());

        assert!(!cleanup_dir.exists());
    }

    #[test]
    fn test_cleanup_nonexistent_directory() {
        // Should not panic
        cleanup_directory("/nonexistent/directory");
    }

    #[test]
    fn test_commit_backup() {
        let dir = TempDir::new().unwrap();
        let backup_dir = dir.path().join("backup");
        fs::create_dir_all(&backup_dir).unwrap();
        fs::write(backup_dir.join("file.txt"), "backup").unwrap();

        commit_backup(&backup_dir.to_string_lossy());

        assert!(!backup_dir.exists());
    }
}
