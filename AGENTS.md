# AGENTS.md

Repository-specific instructions for coding agents working in this repo.

## Build Output Rule

- Never create an `artifacts` folder anywhere under `Sources/` or any of its subfolders.
- Do not use `Sources/**/artifacts` as a custom output path, intermediate path, restore path, publish path, or scratch directory.
- This repository can break compilation when extra `artifacts` folders appear inside `Sources/`.
- If you need isolated build output, temporary restore state, or experimental compile output, use a temp directory outside the source tree.
- Good options are the OS temp directory, `/tmp/...`, or another folder outside `/mnt/d/Work/YoloEase/Sources`.
- Existing `bin/` and `obj/` folders created by the normal toolchain are expected. The restriction is specifically about agent-created `artifacts` folders under `Sources/`.

## Project Orientation

- Read `yoloease-summary.md` before non-trivial changes to annotation flow, dataset storage, CVAT compatibility, dataset export, training, prediction, or retraining automation.
- Treat `yoloease-summary.md` as a living architecture orientation document. Update it in the same change when a meaningful workflow, storage model, project boundary, or major entry point changes.
- Keep architecture docs high-level and current. Do not turn summary docs into changelogs or lists of small refactors.
- Prefer preserving the main system seams: source data vs project-owned synced data, annotation backend vs export format, raw annotations vs YOLO-ready datasets, and current model predictions vs next batch selection.
- Keep third-party or shared dependency submodules untouched unless the user explicitly asks to modify them.

## Code Organization

- Keep public surface area intentional and small. Prefer `internal` concrete classes by default when a type is not part of the consumer-facing contract.
- Prefer interfaces or focused abstractions for services that need to be replaced, tested, hosted differently, or configured by callers.
- Prefer constructor injection for service dependencies. Keep object graph construction in registration/bootstrap code rather than spreading `new` service construction through feature logic.
- Keep shared CVAT contracts and generated CVAT-facing code in `YoloEase.Cvat.Shared`; keep app orchestration and UI behavior in `YoloEase.UI`; keep regression coverage in `YoloEase.Tests`.
- Use structured serializers/parsers for XML, JSON, YAML, project files, and CVAT exports. Avoid regex or ad hoc string slicing for structured formats unless the tradeoff is deliberate and documented.
- Prefer named constants for protocol names, file names, limits, storage folder names, timeout values, and sentinel values when they are part of a durable contract.

## Documentation And Comments

- Add XML documentation comments to public interfaces, public classes, public records, public methods, and public properties that are part of app, plugin, configuration, storage, or integration contracts.
- Add XML documentation comments to internal records, structs, options, and DTOs when they encode durable state, serialized shape, lifecycle ownership, or cross-component contracts.
- Keep XML comments useful: explain purpose, ownership/lifetime expectations, important property meanings, and behavior callers or maintainers need to know quickly.
- When modifying a documented public contract or durable internal contract, update the XML comments in the same change.
- Use normal code comments for invariants, ownership/lifetime rules, non-obvious threading or async behavior, file/storage contracts, and compatibility decisions. Do not add comments that merely restate syntax.

## Testing Rules

- Put YoloEase regression tests in `Sources/YoloEase.Tests` unless a more specific test project is introduced.
- Keep tests focused and small. Split fixtures by behavior area instead of growing one broad fixture that covers unrelated workflows.
- New public API, storage format changes, annotation import/export behavior, prerequisite/toolchain behavior, dataset conversion behavior, training orchestration changes, and bug fixes should land with focused tests unless there is a clear reason not to.
- Give non-trivial NUnit tests a readable scenario shape:
  - use CamelCase `Should...` test method names
  - add an XML doc comment with `WHAT:` and `HOW:` when the scenario is not obvious from the method name
  - use explicit `// Given`, `// When`, and `// Then` sections in the method body
  - use additional `// When` and `// Then` pairs for intentional multi-phase scenarios
- Prefer deterministic tests over live service tests. Tests must not call real CVAT servers, cloud services, production databases, developer-local databases, or real roaming config unless they are explicitly marked as integration tests and kept disabled or separately categorized by default.
- Prefer code-driven setup through test helpers, temp directories, in-memory data, mocks, or DI overrides instead of editing shared machine state or repo-local config files.
- Isolate filesystem state per test. Use OS temp directories or test output directories, clean them up in `finally`/`Dispose`, and enforce timeouts for tests that start processes or can hang.
- If tests need diagnostic output, prefer the repo's logging pipeline or focused test artifacts under a non-`artifacts` folder name such as `TestArtifacts`. Do not use `Sources/**/artifacts`.

## Logging And Diagnostics

- Add meaningful logs around app startup, project load/save, sync, CVAT calls, offline annotation persistence, dataset conversion, prerequisite remediation, external process execution, training/prediction runs, retries, fallbacks, and failure paths.
- Prefer logs that let someone reconstruct what the app tried to do, what environment or paths it saw, and why it chose a branch or failed.
- Avoid noisy logs with no diagnostic value. Do not replace structured logging with ad hoc `Console.WriteLine` in production or test code.
- When running UI or external-process diagnostics, check logs before guessing. Preserve useful log paths and screenshots when reporting failures.

## UI Style Rules

- Reuse the shared `ye-panel`, `ye-panel-header`, `ye-panel-body`, `ye-panel-meta`, and `ye-panel-stack` classes for compact inspector/settings/result panels. The task annotation Shapes/Labels panes are the reference style.
- Keep operational panels dense and restrained: small headers, clear left accent, low-contrast borders, and no nested card stacks.
- Keep GoldenLayout tab labels and routine table text regular or medium weight. Avoid bold tab names and avoid heavy bolding in data panes unless it marks a true section header or important state.
- On the prerequisites page, keep the header row ordered as title/status, `Check at startup`, then action buttons, with the startup toggle visually close to `Check all`.
- Prerequisite rows should be expanded by default so details and dependency blockers are visible without extra clicks.

## UI Testability And Reactivity

- When adding or refactoring Razor pages/components that may be tested, add stable `data-testid` hooks for page roots, primary forms, primary actions, important empty/error/success states, and major sections that a smoke test would assert.
- Prefer accessible labels, text, and roles for user-facing controls, but do not make tests depend on generated DOM shape or visual CSS classes alone.
- For Blazor reactive components, prefer existing project patterns such as `RefreshableReactiveObject`, binder-backed local state, and direct binding to reactive objects over trivial proxy properties that only forward to `DataContext`.
- Do not assume reactive `DataContext` chains stay non-null and unchanged for an entire render. Mirror chained values into null-safe local reactive state when a component renders from them repeatedly.
- For drag/drop or pointer-heavy UI, treat hit testing and feedback as part of the feature. Keep drop target geometry stable, make the visible target match the actual hit area, and add focused temporary diagnostics before escalating to JavaScript changes.
