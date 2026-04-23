---
description: "Sync latest master, work on a fresh branch, create or update a PR, inspect CI and PR feedback, then ask the user what to do next."
---

# PR workflow

Use this prompt for end-to-end repository work that should start from the latest `origin/master` on a new branch and finish with a PR update.

## Inputs

- Requested work: ${input:task:Describe the bug, feature, PR refresh, or other work item to handle}
- Related issue or PR: ${input:reference:Optional issue/PR URL, number, or extra GitHub context}

## Workflow

1. Sync the latest `origin/master` and create a fresh working branch from updated `master` unless the user explicitly asks to reuse an existing branch or continue on an existing PR branch.
2. Investigate the requested work, make the necessary code or documentation changes, and keep the scope limited to the task.
3. Run the repository's existing validation commands that are relevant to the touched area.
4. Push the branch and create or update the pull request.
5. Inspect the PR's CI status. If any check fails, read the failing job logs, fix the reported problems, and push follow-up commits.
6. Review the PR conversation, review comments, and related discussion. If they call for targeted code changes, make those changes and update the PR instead of only replying in text.
7. After the work is complete, use the `ask_user` tool to ask the user what they want done next.

## Additional guidance

- Prefer a clean branch from latest `master` for new work.
- If the task is blocked by missing permissions, an external outage, or ambiguous requirements, explain the blocker clearly.
- Do not skip the CI/log review or the PR discussion review when this workflow is invoked.
