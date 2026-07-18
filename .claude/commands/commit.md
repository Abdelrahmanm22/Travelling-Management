---
description: Review, summarize, and commit the current Git changes with confirmation
---

You are helping the user commit their current Git changes. Follow these steps in order:

1. **Show status** — Run `git status` and present the list of changed/untracked files.
2. **Analyze the diff** — Run `git diff` (unstaged) and `git diff --staged` to understand what actually changed. Read the diffs, don't just skim filenames.
3. **Summarize** — Give a short, clear summary of the changes grouped by intent (e.g. "Added X", "Fixed Y", "Refactored Z"). Keep it scannable.
4. **Stage everything** — Run `git add .`
5. **Generate a commit message** — Based on the actual diff (not the filenames alone), write one meaningful commit message:
   - A concise imperative subject line (<= ~72 chars).
   - If the change is non-trivial, add a short body explaining the *why*.
   - Do NOT add a co-author / "Generated with" trailer.
6. **Ask for confirmation** — Show the proposed commit message to the user and ask them to confirm before committing. Do not commit until they approve. If they request edits, revise the message and ask again.
7. **Commit** — Once confirmed, run: `git commit -m "<the approved message>"` (Use a HEREDOC via `git commit -F -` if the message has a multi-line body.)
8. **Show the result** — Run `git log -1 --stat` (or `git status`) and report the commit hash and what was committed.

Notes:
- If `git status` shows no changes, stop and tell the user there is nothing to commit.
- This repo is on Windows; the Bash tool runs Git Bash (POSIX sh).
