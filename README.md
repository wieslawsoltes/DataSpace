# DataSpace

**A modular Access-style database studio for the browser and desktop, built with Uno Platform and SkiaSharp.**

[![Build, test and deploy](https://github.com/wieslawsoltes/DataSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/DataSpace/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[Browser application](https://wieslawsoltes.github.io/DataSpace/) · [Architecture](docs/ARCHITECTURE.md) · [Compatibility](docs/COMPATIBILITY.md) · [Performance](docs/PERFORMANCE.md) · [Contributing](CONTRIBUTING.md)

DataSpace brings an Office-style ribbon, searchable object navigation, tabbed objects, editable datasheets, graphical queries, form design, report previews and relationship diagrams to a shared .NET codebase. The browser is the actual Uno/Skia application compiled to WebAssembly—not an HTML mockup or a separate front end.

> **0.2.0-preview.1:** an independent Access-style implementation, not complete or verified pixel-for-pixel Microsoft Access parity. Native `.accdb`/`.mdb`, ACE/Jet, VBA and the full Access feature set are not supported. Read the [compatibility matrix](docs/COMPATIBILITY.md) before planning a migration.

## Workspace

| Area | Implemented workflows |
| --- | --- |
| Shell | Red title bar, quick-access commands, grouped ribbon, File backstage, searchable objects, tabs, record navigation and explicit errors. |
| Tables | Typed fields, insert/update/delete, required/default/primary/unique constraints, atomic undo/redo, detached field design and ordered single/composite index editing. |
| Datasheets | Visible-cell Skia drawing, lazy row materialization, range selection, keyboard navigation, native text editing, TSV clipboard, sorting, resizing, filtering, debounced search and cached totals. |
| Queries | SQL and parameter editing, native QBE grid, draggable source cards, join properties, criteria/OR rows, grouping, SELECT SQL round trips and read-only result grids. |
| Query engine | Streaming SELECT/TOP, transient equality hash joins, supported expressions/aggregation, UNION/UNION ALL, saved-query sources and confirmed atomic action statements including INSERT SELECT. |
| Forms | Bound record entry, text/YesNo controls, navigation, snapped control drag/resize, creation/deletion and geometry/caption properties. |
| Reports | Table/query source, columns, title/orientation, pagination, zoom and Skia PDF export through the same page renderer. |
| Relationships and macros | Referential checks/cascades, draggable relationship diagrams, and explicitly invoked ordered workspace macro actions. |
| Files | Versioned `.dspace` JSON, CSV import/export, desktop file adapters, transactional IndexedDB, cross-tab version checks and explicit backup exports. |

The Northwind-style initial workspace contains demonstration data. Use **File → New** to create a database or **External Data → Text File** to import CSV. In a query, switch between **Design View**, **SQL View** and **Datasheet View**. Compound/action SQL stays in SQL View when it cannot be represented by the SELECT designer. In table Design View, **Indexes** opens the reusable ordered index editor.

## Build and run

The repository pins **Uno.Sdk 6.7.30**, matched **SkiaSharp 4.152.1** packages and the **.NET 10** SDK family. Managed and native Skia versions must remain aligned.

```bash
git clone https://github.com/wieslawsoltes/DataSpace.git
cd DataSpace
dotnet workload install wasm-tools

dotnet test tests/DataSpace.Tests/DataSpace.Tests.csproj -c Release
dotnet run --project src/DataSpace.App/DataSpace.App.csproj -f net10.0-desktop

# Publish the real browser application
dotnet publish src/DataSpace.App/DataSpace.App.csproj \
  -c Release -f net10.0-browserwasm -o artifacts/publish
python3 scripts/stage-site.py
python3 -m http.server 8080 --directory artifacts/site
```

Open `http://localhost:8080/`, not `file://`. Desktop execution requires the selected Uno backend's native dependencies and display server. The browser build uses a relative base path and does not require cross-origin-isolation headers or WebAssembly threads.

## Independently reusable libraries

| Package | Responsibility |
| --- | --- |
| `DataSpace.Core` | Model, typed values, validation, record/schema/index transactions, history, snapshots and document codec. |
| `DataSpace.Query` | SQL parsing/evaluation, QBE model/translation, query statistics and lazy identity-preserving table views. |
| `DataSpace.Storage` | CSV, optimistic storage contracts/adapters and asynchronous save-session coordination. |
| `DataSpace.Rendering` | Independent Skia datasheet, form, relationship, report and icon renderers. |
| `DataSpace.Controls` | Reusable Uno ribbon/navigation/datasheet, object designers, resource styles and optional full workspace shell. |

None depends on `DataSpace.App`. The thin app injects platform file dialogs and browser interop. CI packages all five libraries; the tag workflow attaches packages to GitHub Releases without publishing to NuGet.org. The controls library uses a portable `net10.0` package asset and is compiled through both app heads.

### Engine usage without Uno

```csharp
using DataSpace.Core;
using DataSpace.Query;

var workspace = new DatabaseWorkspace(new DatabaseDocument { Name = "Inventory" });
workspace.Edit("Create table", document => ObjectFactory.CreateTable(document));
workspace.Edit("Add record", document => RecordOperations.Insert(
    document.Table("Table1"), new Dictionary<string, string?> { ["Title"] = "Notebook" }));

var view = TableView.Open(workspace.Document, "Table1", sortField: "Title");
var firstPage = view.ReadPage(0, 50);
workspace.UpdateRecords("Edit title", "Table1", [new(firstPage[0].Id, "Title", "Drawing pad")]);
var result = new QueryEngine().Select(workspace.Document,
    "SELECT ID, Title FROM Table1 UNION ALL SELECT 0, 'Unassigned' ORDER BY Title");
workspace.Undo();
```

Only mutate live data through `DatabaseWorkspace.Edit` or `UpdateRecords`. Public models remain mutable for construction/serialization, but direct mutation bypasses history, validation and cache guarantees. Virtual-view records are detached display snapshots, not backing storage. Use `TableView.Select` for the compatible eager result API.

### Embed the workspace

```csharp
var workspace = new DatabaseWorkspace(SampleDatabase.Create());
var session = new WorkspaceSession(workspace, new MemoryWorkspaceStore());
await session.InitializeAsync();
var view = new DatabaseWorkspaceView(workspace)
{
    StorageIsDirty = () => session.IsDirty,
    SaveDatabaseAsync = () => session.SaveAsync()
};
// Put view in your Uno page/window. Inject ImportTextAsync and ExportFileAsync
// for your file-dialog or document-management integration.
```

The embedding example uses `DataSpace.Core`, `DataSpace.Storage` and `DataSpace.Controls`. Merge `ms-appx:///DataSpace.Controls/Themes/OfficeResources.xaml` for supplied styling. Individual controls and designers do not require the shell. Dispose owned views, renderers and sessions when their host is finished.

## Performance and verification

Targeted edits avoid whole-database JSON copying, lazy datasheets materialize visible rows, totals are snapshot-cached, and eligible joins use transient hash lookups. [Performance documentation](docs/PERFORMANCE.md) records reproducible managed benchmarks, work counters and remaining limits. Those measurements are not browser/GPU/frame-rate claims.

```bash
dotnet run --project benchmarks/DataSpace.Benchmarks/DataSpace.Benchmarks.csproj \
  -c Release -- artifacts/performance.json
```

`build.yml` runs managed regressions and benchmarks, compiles desktop, publishes WebAssembly, exercises real IndexedDB and browser interactions, packages the libraries and deploys verified `main` builds. PRs never deploy. Exact source, TRX, performance JSON, packages, screenshots and diagnostics are retained as artifacts. See [CONTRIBUTING.md](CONTRIBUTING.md) for browser-test commands.

`release.yml` verifies version tags and attaches package, browser and source archives with checksums to GitHub Releases. Creating a workflow does not imply that a release has been published; check the release history and CI results.

## Save and protect work

**Ctrl+S** commits the active editor and saves locally. Browser data belongs to the origin/profile; clearing site data or losing a profile may remove it. **Export a `.dspace` backup.** Data is not encrypted or uploaded to a cloud database by the storage adapter.

IndexedDB saves compare and replace the expected version inside one transaction. A conflicting tab fails instead of silently overwriting another window; export its work before reloading. The previous committed browser generation is retained, but no recovery-management UI or automatic merge exists. Invalid stored documents are not silently replaced, failed validation preserves editor drafts, and edits made while saving remain dirty.

## License and attribution

MIT licensed. Uno Platform, SkiaSharp and transitive dependency notices remain applicable. No Microsoft Access source, proprietary application icons or Microsoft font files are bundled. Microsoft Access is a Microsoft trademark; DataSpace is independent and not affiliated with or endorsed by Microsoft.
