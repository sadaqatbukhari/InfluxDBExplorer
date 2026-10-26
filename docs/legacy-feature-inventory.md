# Legacy feature inventory and parity status

Source analysed: `src/CymaticLabs.InfluxDB.Studio` (WinForms, 3.0.7), about 14,300 lines, of which `AppForm.cs` is 2,085.
Replacement: `src/Tickbird.Desktop` (Avalonia) over `src/Tickbird.Core`.

Status key: **Done** = ported and covered by unit/headless tests; **Done (unverified live)** = ported but not yet exercised against a real InfluxDB server; **Open** = not yet built.

## Main window

| Legacy feature | Status | Notes |
| --- | --- | --- |
| Menu: File (Import/Export Settings, Exit) | Done | Import merges by connection ID; export omits passwords and tokens |
| Menu: Connections (Refresh F5, Manage) | Done | |
| Menu: Query (New, Open, Save, Run Ctrl+R, Show Queries) | Done | Ctrl+O / Ctrl+S / Ctrl+R / F5 shortcuts kept |
| Menu: Settings (Time Format, Date Format, Allow Untrusted SSL) | Done | Persisted; TLS flag reaches both SDK clients through `InfluxDbClientFactory.AllowUntrustedSsl` |
| Menu: Help > About (version, update check, release link) | Done (unverified live) | |
| Toolbar, 22 buttons in 4 groups with legacy icons and tooltips | Done | Enable/disable port of `UpdateUIState` (`CommandAvailability`) |
| Connection tree (connection / database / measurement), lazy load, icons | Done (unverified live) | |
| Tree context menus (connection, database, measurement) | Done | |
| Tabbed workspace with close button, Close / Close All But This / Close All | Done | |
| Tab title capped at 300 px with full-title tooltip | Done | |
| New Query always opens an independent tab | Done | Unique ID per call |
| Show Queries focuses the existing tab for the same connection | Done | Keyed by connection ID, not display name |
| Status bar and startup update notice (15 s) | Done (unverified live) | |
| Manage Connections dialog (create, edit, remove, connect) | Done | Connect disabled for already-active connections |
| Connection dialog (1.x/3.x, host, port, db, user, password/token, SSL, Test, Ping) | Done (unverified live) | Port switches 8086/8181 with server type |
| Startup: open Manage Connections after a short delay | Done | |
| Auto-import of legacy settings on first run | Open | Available manually via File > Import > From Tickbird 3.x |

## Database operations

| Legacy feature | Status |
| --- | --- |
| Create / Drop Database (with `_internal` protection, duplicate check, confirmation) | Done (unverified live) |
| Drop Measurement / Drop Series (with confirmation) | Done (unverified live) |
| Show Series (tag columns built from series keys) | Done |
| Show Tag Keys / Tag Values (tag key dropdown) | Done (unverified live) |
| Show Field Keys, retention policy dropdown (1.x only; default first, falls back to first policy with fields) | Done (unverified live) |
| Export CSV/JSON, all or selected rows, from every list tab | Done |
| Show Retention Policies: create / alter / drop, default-policy fallback to autogen | Done (unverified live) |
| Show Continuous Queries: list, view query, create, drop, backfill | Done (unverified live) |
| Run Backfill dialog (source, destination, interval, from/to, fill, tags, filters, subquery) | Done (unverified live) |
| Show Users: create, edit admin flag, password, drop; grants: grant, edit, revoke | Done (unverified live) |
| Show Queries (running queries) with Kill Query | Done (unverified live) |
| Show Statistics (category dropdown, one tab per series) | Done (unverified live) |
| Show Diagnostics | Done (unverified live) |
| InfluxDB 3 hides unsupported legacy commands | Done |

## Query editor and results

| Legacy feature | Status | Notes |
| --- | --- | --- |
| Editor with syntax highlighting | Done | AvaloniaEdit with an InfluxQL colorizer driven by the shared `InfluxQL.xml` |
| Completion: keywords, functions, measurements, fields, tags (Ctrl+Space and after SELECT/FROM/WHERE/GROUP BY/ORDER BY) | Done (unverified live) | `CompletionProvider` in Core |
| Run selection, otherwise whole editor text | Done | |
| Comment / uncomment selected or current lines | Done | |
| Open / Save query file | Done | |
| Starter query per server type | Done | |
| Result tabs ("Results N" / "Group N"), row count and response time | Done (unverified live) | |
| Results grid: row number column, tags line, date/time formatting | Done | |
| Copy cell / row / selected rows, export all or selected as CSV or JSON | Done | |
| Large result handling | Open | Rows are proxy objects formatted on demand, but the grid still receives one object per row. Needs the benchmark fixtures from the plan before any claim about 100k+ rows |

## Not yet done

* Credential store (passwords are still plain text in `settings.json`, as in the old app).
* Write-points workflow (exists on the `feature/WritePoints` branch, not in master).
* Real-server integration tests for InfluxDB 1.x and 3.x.
* macOS and Linux runs, packaging, installers, CI.
* Startup first-run import, About dialog links, accessibility and display-scaling checks.
