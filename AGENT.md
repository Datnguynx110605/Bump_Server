# Agent Operating Rules

## Session Initialization

- ALWAYS read `CLAUDE.md` first then read `AGENT.md` before performing any work in every session.
- ALWAYS begin every session in this order:
  1. Read `CLAUDE.md` in ~/.claude
  2. Read `AGENT.md`
  3. Inspect the codebase and repository state.
  4. Read project documentation.
  5. Understand the project before proposing or implementing changes.
- NEVER commit or push without explicit user approval.

## Repository Understanding and Documentation

- MUST inspect the current working tree and existing uncommitted changes before modifying files.
- MUST discover and read all Markdown files under the repository's `doc/` directory before starting project work.
- MUST follow `doc/project-rules.md` as the authoritative source for folder responsibilities, naming, OOP/DDD, and persistence/cross-cutting conventions; new or changed code MUST conform to it.
- MUST identify and follow documented project rules, architecture, system design, conventions, and constraints.
- If documentation is missing, incomplete, inconsistent, or outdated, MUST inspect the repository thoroughly to infer the actual design and behavior.
- After understanding the repository, MUST update the relevant documentation before or alongside implementation.
- Documentation updates MUST reflect the existing system accurately.
- Documentation updates MUST NOT invent unsupported behavior.

## Refactoring

- NEVER refactor the codebase without explicit user approval.
- Before requesting refactor approval, MUST explain:
  - why the refactor is needed;
  - the proposed solution;
  - expected benefits and optimizations;
  - affected areas;
  - risks and migration impact.
- MUST wait for explicit approval before applying any refactor.
- Small changes required strictly for the requested implementation are allowed only when they preserve the existing design and are not broader refactoring.

## Git Restrictions

- NEVER run `git commit`.
- NEVER run `git push`.
- NEVER create commits, tags, releases, or pull requests.
- Git inspection commands such as `git status`, `git diff`, and `git log` are allowed.
- MUST NOT discard or overwrite existing user changes.

## Terminal Execution

- Build and test commands are allowed.
- Before terminating it, inspect whether the command is still making measurable progress when possible.
- MUST NOT run the application, development server, preview server, background service, watcher, or equivalent runtime command without an explicit user request and approval.
- Before executing any command, MUST determine whether it builds, tests, or runs the project.
- If a build or test command produces no meaningful output or progress beyond its normal expected duration, treat it as potentially stalled.
  1. terminate it immediately;
  2. retry it once;
  3. if the retry is also slow or unresponsive, terminate it;
  4. stop further attempts and report the command, observed behavior, and available output.
- MUST NOT leave background processes running.

## Communication and Completion

- MUST state assumptions when repository evidence is incomplete.
- MUST ask for approval before refactoring or running the project.
- MUST report files changed, checks performed, test results, and unresolved risks.
- NEVER claim that a build or test passed unless it was actually executed successfully.
