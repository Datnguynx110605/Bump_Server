# Claude Code Operating Rules

## Session Initialization

- ALWAYS read this `CLAUDE.md` before performing any work in every session.
- ALWAYS inspect the available Superpowers plugin or skills before starting.
- ALWAYS inspect the available Ponytail plugin (skills, commands, and active mode) before starting.
- ALWAYS begin every session in this order:
  1. Read CLAUDE.md.
  2. Inspect available Superpowers skills.
  3. Inspect available Ponytail skills, commands, and active mode.
  4. Inspect the codebase and repository state.
  5. Read project documentation.
  6. Activate the applicable Superpowers workflow.
  7. Understand the project before proposing or implementing changes.
- Superpowers is the mandatory execution standard, not an optional recommendation.
- Ponytail is the mandatory code standard, not an optional recommendation, for coding tasks.
- When a Superpowers workflow requires a separate specification file but the user or task scope prohibits additional files, MUST preserve the design and review gates in the conversation without creating that file.
- MUST follow all applicable Superpowers skills and workflow gates.
- Superpowers is the mandatory working methodology. Repository-specific rules in this file override only conflicting execution details, such as Git operations, documentation locations, and commands requiring user approval.
- MUST keep all non-conflicting Superpowers workflow gates mandatory.
- MUST keep all non-conflicting Ponytail rules mandatory.
- Ponytail findings that call for deleting, simplifying, or restructuring EXISTING code (not code newly written in the current session) are treated as a refactor proposal and MUST go through the "Refactoring" approval gate below.
- NEVER commit or push, even when a Superpowers skill normally instructs the agent to commit.

## Repository Understanding and Documentation

- MUST inspect the current working tree and existing uncommitted changes before modifying files.
- MUST discover and read all Markdown files under the repository's `doc/` directory before starting project work.
- MUST identify and follow documented project rules, architecture, system design, conventions, and constraints.
- If documentation is missing, incomplete, inconsistent, or outdated, MUST inspect the repository thoroughly to infer the actual design and behavior.
- After understanding the repository, MUST update the relevant documentation before or alongside implementation.
- Documentation updates MUST reflect the existing system accurately.
- Documentation updates MUST NOT invent unsupported behavior.

## Implementation Standards

- MUST NOT implement anything until the project and the affected execution flow are understood.
- MUST follow the existing codebase architecture, control flow, data flow, data model, abstractions, and dependency direction.
- MUST match existing formatting and conventions, including naming, files, folders, classes, functions, method calls, line breaks, spacing, imports, error handling, and test structure.
- MUST prefer focused, minimal changes.
- MUST NOT introduce unrelated cleanup or architectural changes.

## Refactoring

- NEVER refactor the codebase without explicit user approval.
- This includes refactors proposed by the Ponytail plugin (`ponytail-review`, `ponytail-audit`) that target existing code rather than code newly written in the current session.
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