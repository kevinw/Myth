# Telegram coding agent (idea, 2026-08)

The idea: message a private Telegram bot from a phone, have the Mac Studio create
an isolated Myth worktree and ask Codex to implement the request, then receive a
summary and screenshot back in Telegram. The first version should stop at a local
commit; pushing a branch or opening a PR requires an explicit follow-up approval.

```text
Telegram message
      ↓
local LaunchAgent (long polling, one-job queue)
      ↓
fresh worktree from origin/dev
      ↓
codex exec → build/test → run browser → screenshot
      ↓
Telegram summary + image + local branch/commit
```

## Why Telegram first

- Long polling means the Mac only makes outbound requests; no public endpoint,
  domain, tunnel, or cloud broker is required.
- A private bot can accept requests only from one allowlisted Telegram chat ID.
- Acknowledgements, progress, questions, screenshots, and approval buttons all fit
  naturally in the same conversation.
- This avoids email's MIME parsing, spoofing, threading, app-password, and attachment
  handling. Email can feed the same queue later if it still seems useful.

Telegram bots are accounts/usernames, not telephone numbers. Use BotFather to make
one, then keep its token in the worker user's Keychain or a permission-restricted
secret file outside the repository.

## Proposed request shape

Keep v1 deliberately loose:

```text
/myth Add a debug overlay showing the current room ID.
```

Optional commands later:

```text
/status 20260817-004
/cancel 20260817-004
/push 20260817-004
/pr 20260817-004
```

The base branch is hardcoded to `dev` initially. Do not interpret message text as
shell, accept arbitrary repository paths, or add a general command-execution syntax.

## Mac worker

A small Node or C# process runs as a user `LaunchAgent` and:

1. Calls Telegram `getUpdates` with long polling.
2. Rejects messages whose user/chat ID is not the configured owner.
3. Persists the request before acknowledging it (SQLite or one JSON directory per
   job is enough).
4. Fetches `origin` and creates a worktree such as
   `~/AgentWork/Myth/20260817-004` on branch `agent/20260817-004`, based on
   `origin/dev`.
5. Starts `codex exec` in that worktree with workspace-write access, a timeout, and
   JSONL output for progress reporting.
6. Runs the normal build and relevant checks after Codex finishes.
7. Starts the browser build with an isolated browser profile and captures a useful
   screenshot after the game-ready signal.
8. Creates a local commit and replies with the result, tests, diff summary, branch,
   commit, and screenshot.
9. Leaves the worktree intact for review and cleans it up only through an explicit
   command or retention policy.

Start with one concurrent job. Multiple queued jobs are fine; multiple agents
editing related features at once are not yet worth the conflict handling.

Approximate agent invocation:

```sh
codex exec \
  --sandbox workspace-write \
  --json \
  --output-last-message result.md \
  "Implement this request in Myth. Build and test it. Do not push or merge: …"
```

Codex writes progress events to JSONL, which the worker can turn into occasional
Telegram updates without scraping terminal prose. Save the Codex session ID so a
later Telegram reply can resume the job when it asks a real design question.

## Result contract

Every completed job should report:

- Success, partial result, failure, or waiting-for-input.
- A short implementation summary.
- Build/test commands and outcomes.
- Changed-file summary.
- Local branch and commit hash.
- One screenshot when the game can run, otherwise the reason none was captured.
- A short error-log tail on failure.

Do not claim success merely because Codex exited successfully. The wrapper owns the
build, test, screenshot, and clean-worktree checks.

## Screenshot path

Browser capture is the first target. Reuse `tools/run-browser.mjs`, but launch a
fresh browser profile, wait for the loading overlay/game-ready signal, navigate to
the relevant state when possible, and capture the canvas or page. The request may
include a suggested room or reproduction path; otherwise the agent chooses a useful
view and says what it captured.

Native macOS capture is harder: the process needs an active WindowServer session and
Screen Recording permission. Run the worker as a user `LaunchAgent`, not a system
`LaunchDaemon`. The user must remain logged in and the Mac must be awake. Avoid
automatic login merely to make screenshots work.

## Safety boundaries

A worktree isolates Git state, not the rest of the Mac. Prefer a dedicated macOS
user with:

- No personal documents, browser sessions, SSH keys, or personal Keychain access.
- Separate Codex credentials and a narrowly scoped GitHub credential.
- Write access only to its worktrees, job data, and required caches.
- No push permission in v1. Later, expose push/PR as a separate approval action.
- One job at a time, with wall-clock timeout and disk-retention limits.
- A hardcoded repository, worktree root, and base-branch allowlist.
- No automatic merge, force-push, deletion, or execution of commands received in
  chat.

Treat the feature request as untrusted prompt input even though the chat is private.
The wrapper, not the prompt, enforces filesystem locations and permitted lifecycle
actions.

## Staged implementation

### 1. End-to-end skeleton

- Create bot and record the owner chat ID.
- Install a LaunchAgent that long-polls and persists jobs.
- Implement acknowledgement, status, timeout, and error replies.
- Create/remove a disposable worktree with a trivial scripted job.

### 2. Codex implementation jobs

- Run `codex exec` with JSONL output in the worktree.
- Stream coarse progress and preserve logs/session ID.
- Build with Debug + `MythTestShip=true` and run `--check-rooms` by default.
- Commit locally on success.

### 3. Visual proof

- Launch the browser with a fresh profile.
- Add a reliable game-ready signal if the current startup path lacks one.
- Capture and upload a screenshot through Telegram `sendPhoto`.

### 4. Approval actions

- Add inline `Push branch` and `Open draft PR` buttons.
- Give the worker only the minimum GitHub permission required for those actions.
- Keep merge manual and outside the bot.

### 5. Optional shared intake

If email is still desirable, route a dedicated address through a small Cloudflare
Email Worker into the same queue. The Mac continues polling outbound; email becomes
another authenticated frontend rather than a second agent implementation.

## Open questions

- Should every successful attempt be committed, or only attempts that pass all
  checks?
- How should a request specify the room/state to screenshot?
- Should the worker upload branches directly, or remain local until `/push`?
- Is a simple job-directory queue sufficient, or is SQLite useful once resumable
  conversations exist?
- How long should completed and failed worktrees be retained?

## Relevant references

- [Codex non-interactive mode](https://developers.openai.com/codex/noninteractive)
- [Telegram Bot API](https://core.telegram.org/bots/api)
- [Cloudflare Email Routing](https://developers.cloudflare.com/email-service/get-started/route-emails/)
