# Tickbird Cross Platform Migration Plan

Prepared 9 October 2026. Baseline: Tickbird 3.0.7. Status: proposed implementation plan.

## Recommendation and expected outcome

Build an Avalonia desktop application on .NET 10 for Windows, macOS, and Linux. Extract the existing database and application logic into shared libraries, rebuild the interface, and keep the current WinForms application available until the replacement passes feature parity and release checks.

The estimated effort is **40–58 developer days, approximately 8–12 working weeks**, for one experienced full-time C# desktop developer. Allow approximately **10–15 working weeks with contingency**, plus any delays obtaining signing credentials, platform hardware, or test servers. A query-focused MVP should be possible in approximately **4–6 weeks**. These are planning estimates based on source inspection, not a fixed delivery commitment. Reestimate after the first week's prototype.

The goal is a dependable desktop database tool with the current behavior preserved across three operating systems. This plan covers architecture, feature parity, settings migration, verification, installers, releases, and rollback. It does not authorize or implement the migration itself.

## Framework decision

| Option | Fit for this application | Decision |
| --- | --- | --- |
| Avalonia | Desktop support for Windows, macOS, and Linux; suitable for a C# application with tabs, trees, dialogs, an editor, and result grids | Recommended, subject to editor and grid prototype |
| .NET MAUI | Mainstream Windows and macOS support; Linux requires an experimental community backend | Use only if accepting the Linux backend's additional maintenance and support risk |
| Keep WinForms | Preserves the existing Windows application but does not deliver the required three-platform interface | Retain during transition and for maintenance |

Microsoft documents MAUI's Linux GTK4 backend as experimental and community supported, without official Microsoft support. MAUI remains an option, but it does not provide the same supported desktop path for all three requested systems. See [MAUI platform backends](https://learn.microsoft.com/en-us/dotnet/maui/developer-tools/platform-backends/?view=net-maui-10.0).

Avalonia documents Windows, macOS, and Linux support with specific version and architecture tiers. Adopt a tested support matrix instead of promising every OS release. See [Avalonia supported platforms](https://docs.avaloniaui.net/docs/supported-platforms). For the initial Linux release, use the default X11 or XWayland path; native Wayland remains a separate qualification task. See [Avalonia Linux guidance](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).

Select and pin an Avalonia version after the prototype. Validate the editor, grid, themes, SDKs, and packaging against that exact version before adopting it. Record the framework and control decisions in an architecture decision document.

## Current application and migration boundaries

The current project targets `net10.0-windows` and uses WinForms with Syncfusion editor, grid, theme, and other controls. `AppForm` coordinates substantial connection, navigation, command, status, and error handling behavior. Moving the existing forms into another project will not make them cross-platform.

The database layer is a stronger reuse candidate, but it needs separation from the interface. Both database client implementations currently obtain TLS preferences through `Studio.AppForm.Settings`. Settings persistence also reports errors through the main form. These dependencies must be removed before the libraries can serve another desktop framework.

| Existing area | Migration treatment | Main work |
| --- | --- | --- |
| Data models and client abstraction | Reuse and extract | Remove UI dependencies; clarify capabilities, lifecycle, and cancellation |
| InfluxDB 1.x and 3 clients | Reuse with adapters | Inject transport and TLS options; verify SDK behavior on each OS |
| InfluxQL and SQL completion logic | Reuse algorithms and resources | Return framework-independent suggestions; replace editor event integration |
| Settings and legacy imports | Preserve behavior through a new service | Versioned schema, platform paths, credential references, atomic persistence |
| GitHub update checker | Reuse detection logic | Platform browser opening, release asset selection, preview channel policy |
| WinForms forms and Syncfusion controls | Rebuild | Avalonia views, commands, styles, keyboard handling, and dialogs |
| Custom tabs and Windows interop | Replace | Framework layout and rendering; remove Win32 messaging |
| Virtual result adapter and formatting | Reuse where practical | Adapt to selected grid; preserve exports and avoid duplicate result storage |
| Windows installer | Maintain and extend release process | Add macOS and Linux artifacts and platform checks |

Scope includes the existing database operations and administration features wherever the selected server supports them. InfluxDB 3 must not be given controls for unsupported legacy operations. Mobile, browser delivery, new database engines, automatic installation of updates, and unrelated product features are excluded.

## Target architecture

Proposed projects, using the repository's existing namespace conventions where useful:

| Project | Responsibility |
| --- | --- |
| `Tickbird.Core` | Models, client contracts, capability descriptions, result types, and validation |
| `Tickbird.Application` | Connection workflows, metadata loading, query execution, completion, export, and tab identity rules |
| `Tickbird.Infrastructure` | InfluxDB SDK adapters, HTTP transport, persistence, credentials, logging, and GitHub release access |
| `Tickbird.Desktop` | Avalonia views, view models, themes, UI commands, and application composition |
| `Tickbird.Tests` | Core and application unit tests, client integration tests, and settings migration fixtures |
| `Tickbird.Desktop.Tests` | Focused UI automation and platform smoke tests |

Keep the existing WinForms project in the solution during transition. Adopt shared components incrementally through compatibility adapters, with a Windows regression check after each extraction.

The core must not reference WinForms or Avalonia. Application workflows depend on core contracts. Infrastructure implements those contracts. The desktop assembles services and presents their results. UI dispatching belongs at the presentation boundary, and services report structured errors instead of opening dialogs.

Introduce explicit services for settings, credentials, file dialogs, clipboard access, browser opening, notifications, and platform paths. Inject connection and TLS options into clients. Dispose clients when connections close. Cancel outdated metadata requests so delayed responses cannot overwrite the currently selected database or policy.

Define server capabilities centrally and use them for command visibility and execution validation. Include retention policies, series, continuous queries, users, privileges, diagnostics, and query cancellation or termination. Add cancellation tokens where transport and SDK support allow them. Cancelling local waiting must not imply that a server query was terminated; an explicit 1.x kill action must use the correct server query identity.

## Feature parity requirements

These requirements form the acceptance checklist for the replacement application.

| Feature | Required behavior |
| --- | --- |
| Connection management | Create, edit, remove, reconnect, and persist connections; preserve stable IDs, authentication, server type, and TLS preferences |
| Server and database tree | Load and refresh supported nodes asynchronously; preserve selection and show actionable errors |
| New query | Every invocation creates an independent editor tab; users can open as many as resources permit |
| Show queries | Focus the existing Show Queries tab for the same connection when one exists; use connection ID, not its display name, as identity |
| Tab layout | Width follows the title, capped at the existing 300-pixel maximum; full title appears in a tooltip; hovering does not resize or jerk tabs |
| Query execution | Execute selected text when present, otherwise the full editor text; support the appropriate InfluxQL or SQL context |
| Editor behavior | Syntax highlighting, completion, comment actions, query file open and save, familiar shortcuts, and unsaved-change handling |
| Query results | Virtualized display, correct headers and values, scrolling, selection, copy actions, and existing CSV and JSON export choices |
| Fields in InfluxDB 1.x | Retention policy dropdown appears at the top; initially select the default policy if it contains fields, otherwise the first policy with fields |
| Changing retention policy | Reload fields for the chosen policy and measurement; correctly handle empty policies, failures, and rapid switching |
| Fields in InfluxDB 3 | Show the supported schema without a legacy retention policy dropdown |
| Tags and series | Preserve supported key, value, and series browsing and management behavior |
| Database administration | Preserve supported create and drop operations and their confirmation behavior |
| Retention policies | Preserve supported create, alter, drop, and default-policy operations |
| Continuous queries and backfill | Preserve existing supported workflows, validation, and execution feedback |
| Users and privileges | Preserve supported user management, passwords, administrators, grants, and revocation |
| Diagnostics and writes | Preserve supported diagnostics, statistics, ping, and write workflows identified in the baseline inventory |
| Preferences and logs | Preserve date and time formatting, TLS settings, millisecond log timestamps, and useful error details |
| About and updates | About shows the installed version and available update; startup shows a short notification when a newer eligible GitHub release exists |

Create a screen and command inventory during phase 0, linking each current menu, dialog, and shortcut to its replacement and test. Any remaining feature must be marked implemented, intentionally excluded with agreement, or blocked. Do not infer full parity merely because query execution works.

## Editor and results feasibility

The editor and results grid are the highest technical uncertainties and must be proven first.

Use [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) as the initial editor candidate. It is an Avalonia text editor derived from AvalonEdit with an MIT license. Confirm compatibility with the chosen Avalonia release and build a working completion popup before committing to it.

The editor prototype must demonstrate highlighting for both query languages, selected-text execution, completion insertion, keyboard navigation, file operations, Unicode text, and readable light and dark themes. Move the existing completion engine behind an independent interface. Preserve or deliberately update the embedded InfluxQL resource name. Cache schema by connection, database, retention policy, and measurement; invalidate it when context changes.

Prototype Avalonia's DataGrid or another compatible grid with the existing virtual result model. Test wide columns, large row counts, changing schemas, null values, timestamps, multi-selection, clipboard output, and exports. Select a replacement control only after checking licensing, redistribution, maintenance, and performance.

UI virtualization does not make a fully buffered server response stream or bound its memory. Measure transport buffers, result storage, grid adapters, and export allocations separately. Avoid creating a second complete table for display or export. If streaming is needed, verify each SDK's supported path and design cancellation and cleanup around it. Do not introduce automatic pagination or silently rewrite user queries without defined semantics.

Proposed benchmark fixtures are 100,000 rows with 10 columns for the baseline, a wide-column fixture, and a one-million-row synthetic stress case. On a documented reference machine, target ordinary tab and selection feedback within 200 milliseconds when no server work is required. Establish memory and scrolling thresholds from phase 0 measurements; these targets are not claims about current performance.

## Settings and credential migration

Use separate settings for the preview application so it can run beside WinForms without modifying the existing settings file.

1. Define a versioned settings DTO independent of either UI framework. Preserve connection IDs, server type, names, preferences, and connection parameters.
2. Implement platform paths. Defaults are `%APPDATA%\Tickbird` on Windows, `~/Library/Application Support/Tickbird` on macOS, and `$XDG_CONFIG_HOME/tickbird` with `~/.config/tickbird` fallback on Linux. Settings from earlier versions are copied on first launch.
3. On Windows, detect the current JSON settings and supported legacy `user.config` sources. Provide an import summary and save a backup before import. Other platforms receive a portable import workflow.
4. Store passwords and tokens in the platform credential store and retain only references in the new settings file. Evaluate Windows Credential Manager, macOS Keychain, and Linux Secret Service adapters during the prototype.
5. If a Linux credential service is unavailable, use session-only credentials with a clear persistence choice. Do not silently fall back to storing plaintext secrets.
6. Write settings atomically, retain a last-known-good backup, and report corruption without losing usable connection data. Make imports idempotent and prevent duplicate connections.
7. Keep the original WinForms settings unchanged until stable cutover. Default connection exports omit secrets; encrypted secret export is a separately scoped option.

Verify import from real sanitized 3.0.7 fixtures, missing files, old XML settings, corrupt JSON, duplicate names, empty credentials, and unavailable credential storage. Opening the old application after preview use must still work with its original settings.

## Implementation phases and estimates

Estimates are developer effort, include the verification listed for each phase, and assume one experienced developer with part-time platform testing support. They are sequential planning ranges; parallel staffing does not divide them evenly.

| Phase | Deliverable | Effort | Exit gate |
| --- | --- | --- | --- |
| 0 Feasibility and inventory | Three-platform spike, feature inventory, control decisions | 3–5 days | Editor, grid, both clients, and packaging path proven sufficiently to proceed |
| 1 Shared foundation | Portable libraries and compatibility adapters | 5–7 days | Shared code builds without Windows UI references; existing Windows app still works |
| 2 Desktop shell | Connections, navigation, tabs, settings, basic dialogs | 4–6 days | All target systems can connect and navigate with stable tab behavior |
| 3 Query MVP | Editor, execution, results, fields, tags, copy, and export | 9–12 days | Core query workflows pass on both server families and all target systems |
| 4 Administration parity | Remaining supported management operations | 6–9 days | Inventory has no unexplained feature gaps |
| 5 Migration and polish | Imports, credentials, updates, shortcuts, themes, error handling | 5–7 days | Migration and recent regression cases pass |
| 6 Release qualification | Installers, CI, platform testing, preview feedback, documentation | 8–12 days | Release checklist passes and rollback is tested |
| Total | Stable cross-platform candidate | **40–58 days** | Approximately **8–12 working weeks before contingency** |

### Phase 0 Feasibility and inventory

- Inventory screens, commands, shortcuts, server capabilities, settings, and external dependencies.
- Build a disposable Avalonia shell with an editor, result grid, and representative tabs.
- Run a real read-only query against InfluxDB 1.x and 3 using the existing client implementations after minimal isolation.
- Exercise completion and benchmark representative result fixtures.
- Run the spike on Windows, macOS, and a supported Linux distribution; verify SDK native dependencies and browser, clipboard, and file interactions.
- Record control choices, initial platform matrix, remaining risks, and revised estimates. If a key control fails, evaluate alternatives before starting the UI rebuild.

### Phase 1 Shared foundation

- Extract models and client contracts, followed by SDK adapters and reusable workflows.
- Replace main-form settings access with injected options and error reporting contracts.
- Extract formatting, exports, completion, and update detection without changing behavior unnecessarily.
- Add capability descriptors, client disposal, request cancellation, and structured errors.
- Add focused tests around extracted behavior and compatibility adapters.
- Build and smoke-test the current Windows application after each significant extraction.

### Phase 2 Desktop shell

- Establish composition, view models, application lifecycle, logging, and themes.
- Implement connection management, server tree, menus, dialogs, and status feedback.
- Implement unique editor tabs and reusable Show Queries tabs with explicit identity rules.
- Match title width caps, tooltips, icons, focus behavior, and smooth hover rendering.
- Add settings service integration, file dialogs, clipboard, and external browser services.
- Verify keyboard navigation, display scaling, and window behavior across the platform matrix.

### Phase 3 Query MVP

- Implement language selection, highlighting, completion, file operations, and query commands.
- Execute selected or full text, with loading, cancellation, failure, and reconnection states.
- Adapt result storage and virtualization; preserve formatting and copy actions.
- Implement CSV and JSON exports without unnecessary full-result copies.
- Implement fields and tags, including retention policy selection and asynchronous refresh.
- Release an internal preview with the query acceptance suite passing. Administration remains explicitly incomplete at this milestone.

### Phase 4 Administration parity

- Implement database and measurement operations and supported series actions.
- Implement retention policies, continuous queries, and backfill.
- Implement supported users, passwords, administrators, and privileges.
- Complete diagnostics, statistics, write workflows, and running-query management.
- Apply capability checks consistently in menus, dialogs, and services.
- Validate management operations against disposable databases and reconcile every inventory entry.

### Phase 5 Migration and polish

- Complete legacy settings import, backups, schema upgrades, and credential adapters.
- Integrate About and startup update notifications with platform-specific download links.
- Preserve quiet startup behavior when offline or when release checks fail.
- Finish shortcuts, unsaved edits, focus restoration, theme contrast, and accessibility checks.
- Test rapid context switching, disconnection, malformed results, and closing busy tabs.
- Complete recent-fix regression tests and user documentation.

### Phase 6 Release qualification

- Produce installable artifacts and automated builds for the agreed architectures.
- Verify install, launch, upgrade, uninstall, settings persistence, signing, and release asset selection.
- Run server integration, performance, and platform smoke suites.
- Publish an explicitly marked preview, gather focused feedback, and fix release-blocking issues.
- Test rollback and the stable transition from the Windows product.
- Prepare release notes, support matrix, known limitations, checksums, dependency notices, and the stable release candidate.

## Test strategy and platform matrix

Define the minimum supported OS versions at phase 0 using the selected framework's support table and actual test capacity. Proposed first-release coverage is Windows x64, macOS Apple Silicon, and Linux x64. Add Windows ARM64, macOS Intel, or Linux ARM64 only when their dependencies, installers, and tests are qualified. Supporting three operating systems does not automatically require every architecture.

| Test layer | Required coverage |
| --- | --- |
| Unit | Version comparison, capability gating, tab identity, formatting, completion context, export escaping, settings schema and policy selection |
| Integration | Real InfluxDB 1.x and 3 instances; authentication, queries, schema, errors, timeouts, supported administration, TLS, and client disposal |
| UI | Independent New Query tabs, Show Queries focus, hover stability, policy dropdown, editor selection, results, shortcuts, copy and export |
| Platform | Paths, credential storage, file permissions, clipboard, browser opening, display scaling, keyboard conventions, install and upgrade |
| Performance | Large and wide results, scrolling, repeated queries, exports, closing tabs, cancellation, and retained-memory checks |
| Migration | Existing JSON and XML settings, backups, corrupt files, duplicate names, import retries, and old-app compatibility |

Use dedicated disposable databases for destructive integration operations. Maintain deterministic fixtures containing Unicode identifiers, quoted names, multiple retention policies, a default policy without fields, nulls, timestamps, and varied numeric types. Include two different connections with the same display name to expose incorrect tab deduplication.

Mocked services support fast tests, but cannot replace real client and platform qualification. Store benchmark machine details and results so later measurements are comparable. Avoid broad screenshot tests for ordinary logic; use targeted UI checks for layout and interaction regressions.

## Packaging and release workflow

Use one version source for all projects and artifacts. Resolve the release version once before parallel platform builds; do not increment it separately in each build job.

| Platform | Initial delivery | Qualification |
| --- | --- | --- |
| Windows | Self-contained x64 installer using the existing WiX approach where practical | Installation, upgrade, uninstall, application identity, shortcuts, settings, and launch |
| macOS | Signed and notarized application in a DMG for each supported architecture | Mac build and signing environment, permissions, launch, credential access, and Gatekeeper behavior |
| Linux | Tested archive and AppImage candidate for an explicitly supported distribution baseline | Native dependencies, glibc baseline, X11 or XWayland, desktop integration, and credential fallback |

Add Debian or other distribution packages after the initial packaging path is stable, unless required for the first release. A self-contained .NET build still needs the desktop framework's native dependencies. Budget access to macOS hardware or runners and Apple signing credentials before release qualification.

CI should restore pinned dependencies, build shared projects, run unit tests, build the current Windows application during transition, and produce the new platform artifacts. Release jobs run integration and smoke tests, sign where configured, and publish checksums, dependency license notices, and an SBOM. Limit signing and publication credentials to release jobs.

A proposed cross-platform release line is **4.0.0 preview followed by 4.0.0 stable**, because packaging and settings behavior change materially. Confirm the version choice before release. Preview installers use separate application identity, install location, and Windows product identity so they can coexist with 3.x.

For stable Windows replacement, deliberately decide whether to preserve the installed product's upgrade identity. Test that choice against the 3.0.7 installer; do not reuse preview identity accidentally. Continue necessary 3.x maintenance until the replacement is qualified.

The update checker must compare eligible versions, distinguish stable from opt-in previews, and select artifacts for the current OS and architecture. About provides the release link and check action. Startup offers a short notification only when an eligible newer release is available, without delaying startup or installing it automatically. Test missing assets, prereleases, offline operation, malformed responses, and GitHub rate limits.

## Rollout and rollback

1. Run the prototype internally with disposable settings and read-only database workflows.
2. Make the query MVP available as a separate preview; label incomplete administration clearly.
3. Release a feature-complete beta after imports, credentials, and management workflows pass.
4. Run a release candidate through the full platform and upgrade matrix.
5. Publish stable after the release gate is approved by the product owner and test evidence is recorded.

Keep the WinForms source, installer, and release available throughout the preview. Rollback before stable means uninstalling the preview and reopening the old application with unchanged settings. Rollback after stable must include a tested old-version installation path and access to the preserved settings backup. Do not claim installer downgrade support until it has been exercised.

Use small reviewable changes: foundation extraction, shell, query flows, individual administration areas, then packaging. Proposed branch prefix is `codex/`; no branch is created by this plan. Keep Windows maintenance separate from unfinished replacement UI while sharing qualified core fixes when useful.

## Risks and responses

| Risk | Impact | Response and decision point |
| --- | --- | --- |
| Editor completion or shortcuts do not match current behavior | High | Demonstrate the full interaction in phase 0; change control or revise scope before rebuilding screens |
| Grid performs poorly or duplicates result data | High | Benchmark representative data; inspect allocation paths; choose a control and storage model together |
| SDK dependencies fail on a target OS | High | Execute real queries on all target systems in phase 0; verify supported runtime artifacts |
| Main-form dependencies are more widespread than expected | Medium to high | Extract incrementally, retain compatibility adapters, and reestimate after phase 1 |
| Unsupported server features leak into the interface | High | Central capability model plus execution checks and real server tests |
| Settings or credentials fail to migrate | High | Separate preview storage, backups, idempotent import, recovery tests, and unchanged legacy settings |
| Signing or macOS test access is unavailable | High for release | Arrange access early; complete development independently but do not claim signed release readiness |
| Linux distribution differences cause runtime failures | Medium to high | Declare a tested baseline; package and test native dependencies; expand support deliberately |
| Platform focus, clipboard, or scaling behavior differs | Medium | Qualify representative workflows on physical or hosted platform environments throughout development |
| Feature inventory grows after the estimate | Medium to high | Freeze parity scope after phase 0; track additions separately and revise dates openly |

## Ownership and planning assumptions

The estimate assumes one developer experienced in C#, asynchronous services, and desktop UI, plus part-time testing and timely product decisions. Assign a product owner to confirm parity and release readiness, and a platform test owner to collect Windows, macOS, and Linux evidence. One person may fill several roles, but their testing time must remain in the schedule.

Required inputs are representative sanitized settings, test connections for both server families, access to target systems, signing arrangements, and agreement on supported OS versions and architectures. Use the proposed defaults while drafting; resolve them during phase 0 before promising release coverage. External waiting time and new features are outside the base effort estimate.

## First week execution checklist

- Day 1: complete the feature and dependency inventory, record current behavior, and agree on the initial platform matrix.
- Day 2: create a disposable shell and isolate enough client code to connect and query without the Windows main form.
- Day 3: integrate the candidate editor and grid; demonstrate completion, selected-text execution, and large-result display.
- Day 4: run the same workflows on macOS and Linux; check native dependencies, clipboard, paths, and credential options.
- Day 5: record measured results, select controls, identify blockers, and revise the phase estimates and release assumptions.

These are sequencing goals for the 3–5 day spike, not a guarantee that every issue can be solved within a day.

## Stable release acceptance checklist

- [ ] Every inventoried feature has an implemented replacement or an agreed exclusion.
- [ ] New Query always creates a new tab; Show Queries focuses an existing tab only for the matching connection.
- [ ] Tab sizing respects the title and width cap, with no hover jerk.
- [ ] Retention policy selection finds fields correctly and remains stable during rapid changes.
- [ ] Both server families pass query and supported administration tests.
- [ ] Results, completion, copy, exports, and formatting pass functional and performance checks.
- [ ] Settings import preserves connections, credentials, and preferences with a recoverable backup.
- [ ] About and startup update notifications select the correct channel and platform assets.
- [ ] Install, upgrade, uninstall, and rollback pass on every advertised platform and architecture.
- [ ] Signing and notarization are verified for the artifacts advertised as signed.
- [ ] No release-blocking crash, data-loss, credential-loss, or supported-operation failure remains open.
- [ ] Support matrix, known limitations, release notes, checksums, licenses, and test evidence are ready.

The implementation deliverables are the shared libraries, Avalonia application, compatibility adapters, parity inventory, architecture decisions, automated tests, platform installers, migration and rollback instructions, and release qualification evidence.
