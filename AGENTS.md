# Agent Instructions

This file gives guidance to AI/LLM coding agents (Claude Code, Copilot, Cursor, Codex, pi, etc.) working in the Questionable repository. It adapts the ["Artificial Intelligence"/Large Language Model (AI/LLM) usage](CONTRIBUTING.md#artificial-intelligencelarge-language-model-aillm-usage) section of `CONTRIBUTING.md` into rules for the agent and the human driving it.

Read `CONTRIBUTING.md` in full before contributing. Nothing in this file overrides it — if there is a conflict, `CONTRIBUTING.md` wins.

## Ground rules for AI-assisted contributions

Agents operating in this repo, and the humans directing them, must follow these rules whenever the output is intended to become an issue or pull request:

- **Disclose usage.** The initial issue or PR description must state that AI/LLM was used and name the tool/model (e.g. "Claude Sonnet 4.5 via pi", "GPT-5 via Cursor"). Agents drafting a PR body should include this disclosure by default and prompt the user to confirm it.
- **Human review before human review.** All generated code, commit messages, and prose must be read and understood by the submitting human before requesting review from a Questionable maintainer. Agents should assume their output is a *draft* until the human explicitly signs off.
- **The human must be able to respond to review.** If the agent cannot address a review comment, the human must be able to do so manually. Do not open a PR on behalf of a user who cannot defend or modify the code.
- **One AI-assisted PR at a time** for non-maintainers. Before opening a new AI-assisted PR, check that the same author has no other AI-assisted PR open.
- **No "vibe coding".** Purely vibe-coded submissions will be rejected. The human is responsible for what they commit — agents should refuse to push, tag, or open PRs without explicit per-action confirmation from the user.
- **In-file disclaimer for agent-authored files.** Any file whose *initial version* was written end-to-end by an agent (as opposed to targeted edits or partial suggestions inside a human-authored file) must carry a top-of-file disclaimer in the same two-line form used by `.github/workflows/pr-test.yml`:

  ```
  # Authored with LLM assistance, changes must be reviewed and owned by a human.
  # Initial version reviewed and owned by @<github-handle>
  ```

  Adapt the comment syntax to the file's language (`//` for C#, `<!-- -->` for XML/Markdown, `#` for YAML/shell, etc.). Agents adding such a file **must** insert this header themselves and populate `@<github-handle>` with the driving human's GitHub username. If the agent has that username in local/session memory it should use it directly; otherwise it must prompt the human for it before writing the file. Do not use the maintainer's handle as a placeholder, and do not omit the disclaimer on the assumption the reviewer will add it.

  This rule applies only to *new* agent-authored files. Small edits to existing human-authored files do not require a header, and edits to a file that already carries the disclaimer only require updating the second line if a new human takes ownership.

## Scope discipline

From `CONTRIBUTING.md`: Questionable does not want scope creep. Before writing code for a new feature, the human should discuss it in the linked Discord channels. Agents should:

- Prefer fixing the specific bug or implementing the specific, already-discussed feature that was requested.
- Push back (in chat) when a request would meaningfully expand the plugin's scope, and suggest the human raise it in `#questionable-general` or `#questionable-issues` first.
- Not add "while I'm here" refactors, unrelated features, or speculative abstractions to a PR.

## Agent behaviour inside this repo

- **Confirmation before mutation.** Do not run `edit`, `write`, or side-effecting `bash` (git commits, pushes, package installs, formatters that rewrite files, build artifacts) without explicit in-turn permission from the user. Read-only inspection (`ls`, `grep`, `find`, `read`, `git status`/`diff`/`log`) does not require confirmation.
- **Respect the build setup.** Questionable is a Dalamud plugin. Any `dotnet build`/`format`/`test` invocation that touches `Questionable.csproj` (or projects that reference it, e.g. `Questionable.Tests`) must pass `-p:DalamudLibPath=<path>`. See `.github/workflows/release.yml` for the canonical setup.
- **Do not touch generated data bundles** (`QuestPaths/**`, `GatheringPaths/**`) unless the task is explicitly about them.
- **Formatting.** If you run `dotnet format`, run it once against `Questionable.sln` with `-p:DalamudLibPath=...` and `--severity info`, matching CI. Do not add `--verify-no-changes` semantics to unrelated PRs.
- **Commit messages and PR bodies** should describe what changed and why, in the human's voice, and must include the AI-usage disclosure noted above.

## When to stop and hand back

Per `CONTRIBUTING.md`: *"If you reach the point where you feel unwilling or unable to do the above, please close your issue or pull request."* Agents should surface this to the user when:

- The user cannot explain a change the agent produced.
- Review feedback requires understanding the agent has not been able to give the user.
- The change is drifting into scope creep or a large refactor that was not agreed upstream.

## Further reading

The policy is adapted from Homebrew's contributing guidelines. These articles align with this project's position on LLM submissions:

- [Homebrew — Responsible AI Usage](https://github.com/Homebrew/brew/blob/9569699c928bfa1669a2a728dba2fe06cf7864eb/docs/Responsible-AI-Usage.md)
- [Jellyfin — LLM policies](https://github.com/jellyfin/jellyfin.org/blob/bef6e2d2f360557a221d7d8382156e4b62bf2d2b/docs/general/contributing/llm-policies.md)
