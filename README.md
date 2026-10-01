# DataSpace

**A modular Access-style database studio for the browser and desktop, built with Uno Platform and SkiaSharp.**

[![Build, test and deploy](https://github.com/wieslawsoltes/DataSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/DataSpace/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/DataSpace.Core.svg?label=NuGet)](https://www.nuget.org/packages/DataSpace.Core)
[![Downloads](https://img.shields.io/nuget/dt/DataSpace.Core.svg)](https://www.nuget.org/packages/DataSpace.Core)

[Browser application](https://wieslawsoltes.github.io/DataSpace/) · [Architecture](docs/ARCHITECTURE.md) · [Compatibility](docs/COMPATIBILITY.md) · [Performance](docs/PERFORMANCE.md) · [Contributing](CONTRIBUTING.md)

DataSpace brings an Office-style ribbon, searchable object navigation, tabbed objects, editable datasheets, graphical queries, form design, report previews and relationship diagrams to a shared .NET codebase. The browser is the actual Uno/Skia application compiled to WebAssembly—not an HTML mockup or a separate front end.

> **0.2.0-preview.7:** an independent Access-style implementation, not complete or verified pixel-for-pixel Microsoft Access parity. Native `.accdb`/`.mdb`, ACE/Jet, VBA and the full Access feature set are not supported. Read the [compatibility matrix](docs/COMPATIBILITY.md) before planning a migration.

## Workspace

| Area | Implemented workflows |
| --- | --- |
| Shell | Red title bar, quick-access commands, grouped ribbon, File backstage, searchable objects, tabs, record navigation and explicit errors. |
| Tables | Typed fields, insert/update/delete, required/default/primary/unique constraints, atomic undo/redo, detached field design and ordered single/composite index editing. |
| Datasheets | Visible-cell Skia drawing, lazy row materialization, range selection, keyboard navigation, native text editing, TSV clipboard, sorting, resizing, filtering, debounced search and cached totals. |
| Queries | SQL and parameter editing, native QBE grid, draggable source cards, join properties, criteria/OR rows, grouping, SELECT SQL round trips, Find Duplicates/Unmatched builders and read-only result grids. |
| Query engine | Streaming SELECT/TOP, transient equality hash joins, supported expressions/aggregation, UNION/UNION ALL, saved sources, correlated scalar/EXISTS/IN/ANY/ALL subqueries and confirmed atomic actions including INSERT SELECT. |
| Forms | Bound record entry, text/YesNo controls, navigation, snapped control drag/resize, creation/deletion and geometry/caption properties. |
| Reports | Table/query source, columns, title/orientation, pagination, zoom and Skia PDF export through the same page renderer. |
| Relationships and macros | Referential checks/cascades, draggable relationship diagrams, and explicitly invoked ordered workspace macro actions. |
| Files and external data | Versioned `.dspace`, CSV/JSON/SQLite import/export, JSON URL/Pointer sources, paged read-only online sources through an authenticated gateway, transactional IndexedDB and explicit backups. |

The Northwind-style initial workspace contains demonstration data. Use **File → New** to create a database or **External Data → Text File** to import CSV. In a query, switch between **Design View**, **SQL View** and **Datasheet View**. Compound/action SQL stays in SQL View when it cannot be represented by the SELECT designer. In table Design View, **Indexes** opens the reusable ordered index editor.

Crosstab queries have a dedicated **Crosstab Builder**, optional fixed column headings and row totals. The engine also supports atomic `SELECT INTO` make-table queries and constraint-index DDL. Streaming groups and bounded ordered `TOP` selection reduce retained query state. See [query analytics](docs/QUERY-ANALYTICS.md) for syntax, reusable APIs and explicit limitations.

**Find Duplicates** and **Find Unmatched** in the query editor generate ordinary saved SQL. Nested queries support lexical correlations and per-execution caching of eligible independent results, with explicit work limits and null semantics. See [subqueries and Find builders](docs/SUBQUERIES.md). Single-key duplicate details use a grouped key set; composite-key details retain a bounded correlated path.

## External data: SQLite, JSON and online databases

**External Data → New Data Source** opens an Access-style source sidebar and read-only preview. Browse JSON or SQLite files, select a nested JSON array with JSON Pointer, connect to a JSON URL, or use the **Online Database** command for PostgreSQL, MySQL/MariaDB and SQL Server via the self-hosted DataSpace gateway. Page navigation, refresh and a bounded cache avoid importing a whole database merely to inspect it. **Import table** creates a validated, editable local copy; it does not link or write through to the source.

SQLite file operations run in a dedicated, disposable WASM worker in the browser and through `Microsoft.Data.Sqlite` on desktop. The browser assets are pinned and served locally; no third-party CDN is required. Exports create standalone SQLite databases using TEXT/null columns so large integers, precise decimals and source values do not round through JavaScript numbers. Relational preview values are text; JSON signed 64-bit integers and booleans remain typed, while arbitrary-precision numbers and nested objects/arrays are preserved as text.

Online database credentials and allowlisted table definitions are configured on the gateway, **never in the static Pages app**. The UI holds only a session access token. Deploy the gateway separately with HTTPS and least-privilege database credentials; it is not a hosted service supplied by the Pages demo. See [setup, APIs and limits](docs/EXTERNAL-DATA.md).

**Field Options** in the source dialog adds per-column selection, local field names, explicit types, required/unique constraints and existing or generated primary keys. Plans are frozen before import, and errors retain the dialog draft without committing a partial table. Selective import avoids materializing discarded destination fields; source pages are still read in full. See the [field options and limits](docs/EXTERNAL-DATA.md#import-field-options).

**Append to existing local tables:** after importing a source, use **External Data → Append Records** to map the current datasheet view into a destination table. Matching names are preselected, AutoNumber is regenerated unless explicitly mapped, and destination constraints are enforced atomically. The confirmation defaults to Cancel; a successful append is one undo step. See [append behavior and API](docs/APPEND.md).

## Workspace UI extension

Contextual Table Fields, Table Design, Query Design, Form and Print Preview tabs expose working editor actions. Table datasheets save hidden/ordered/frozen columns, row height, font size/bold, alternating shading and gridline options. **Home → Find / Replace** searches literal stored or formatted values in the current view; replacing is atomic, raw-value only, and Replace All requires explicit confirmation. **Help → Navigation Pane / Close Object / Close All** and document-tab context menus manage the workspace. The navigation header filters object types, sorts names, collapses groups and controls session-only hidden objects and click behavior.

See [UI coverage and usage](docs/UI-COVERAGE.md) for exact scope, commands and remaining Access UI gaps. This extension does not implement every Access dialog or control.

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
(cd scripts && npm install --ignore-scripts --no-audit --no-fund)
python3 scripts/stage-site.py
python3 -m http.server 8080 --directory artifacts/site
```

Open `http://localhost:8080/`, not `file://`. Desktop execution requires the selected Uno backend's native dependencies and display server. The browser build uses a relative base path and does not require cross-origin-isolation headers or WebAssembly threads.

## Download

The version-tag release workflow builds self-contained, single-file desktop apps. Check the [release assets](https://github.com/wieslawsoltes/DataSpace/releases) for published versions:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `DataSpace-<version>-win-x64.zip` | `DataSpace-<version>-win-arm64.zip` |
| macOS | `DataSpace-<version>-osx-x64.tar.gz` | `DataSpace-<version>-osx-arm64.tar.gz` |
| Linux | `DataSpace-<version>-linux-x64.tar.gz` | `DataSpace-<version>-linux-arm64.tar.gz` |

Extract and run `DataSpace` (`DataSpace.exe` on Windows). Builds are not code-signed yet. Verify downloads against `SHA256SUMS.txt` and review operating-system warnings; use a reviewed source build when local policy blocks unsigned binaries.

## NuGet packages

DataSpace has seven MIT-licensed library projects, versioned together. CI produces NuGet packages; release workflows also pack symbol packages (`.snupkg`) and version-tag workflows can publish them to [NuGet.org](https://www.nuget.org/packages?q=DataSpace). The two external-source packages are included beginning with preview.4. All seven target `net10.0`. `DataSpace.Core`, `DataSpace.Query` and `DataSpace.Storage` have no UI or graphics dependency, `DataSpace.Rendering` needs only SkiaSharp 4.152, and `DataSpace.Controls` is an Uno Platform (Skia renderer) library that ships one portable `net10.0` asset and is compiled through both the desktop and WebAssembly app heads. None depends on `DataSpace.App`; the thin app only injects platform file dialogs and browser interop. Current versions are previews, so pass `--prerelease`.

```sh
dotnet add package DataSpace.Core --prerelease
```

| Package | Version | Downloads | Description |
| --- | --- | --- | --- |
| [DataSpace.Core](https://www.nuget.org/packages/DataSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/DataSpace.Core.svg)](https://www.nuget.org/packages/DataSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/DataSpace.Core.svg)](https://www.nuget.org/packages/DataSpace.Core) | Typed document model, validation, relational constraints, record/schema/index transactions, history and document codec |
| [DataSpace.Query](https://www.nuget.org/packages/DataSpace.Query) | [![NuGet](https://img.shields.io/nuget/vpre/DataSpace.Query.svg)](https://www.nuget.org/packages/DataSpace.Query) | [![Downloads](https://img.shields.io/nuget/dt/DataSpace.Query.svg)](https://www.nuget.org/packages/DataSpace.Query) | Managed SQL parser and evaluator, QBE/crosstab/Find designs, subqueries, statistics and lazy table views |
| [DataSpace.Storage](https://www.nuget.org/packages/DataSpace.Storage) | [![NuGet](https://img.shields.io/nuget/vpre/DataSpace.Storage.svg)](https://www.nuget.org/packages/DataSpace.Storage) | [![Downloads](https://img.shields.io/nuget/dt/DataSpace.Storage.svg)](https://www.nuget.org/packages/DataSpace.Storage) | CSV interchange, optimistic versioned storage contracts/adapters and save-session coordination |
| [DataSpace.Rendering](https://www.nuget.org/packages/DataSpace.Rendering) | [![NuGet](https://img.shields.io/nuget/vpre/DataSpace.Rendering.svg)](https://www.nuget.org/packages/DataSpace.Rendering) | [![Downloads](https://img.shields.io/nuget/dt/DataSpace.Rendering.svg)](https://www.nuget.org/packages/DataSpace.Rendering) | UI-independent SkiaSharp datasheet, relationship, form, report/PDF and icon renderers |
| `DataSpace.DataSources` | CI package artifact | — | UI-independent JSON/HTTP sources, contracts, cache, atomic-copy import and export |
| `DataSpace.DataSources.Relational` | CI package artifact | — | Native SQLite and configured relational paging; not included in the browser runtime |
| [DataSpace.Controls](https://www.nuget.org/packages/DataSpace.Controls) | [![NuGet](https://img.shields.io/nuget/vpre/DataSpace.Controls.svg)](https://www.nuget.org/packages/DataSpace.Controls) | [![Downloads](https://img.shields.io/nuget/dt/DataSpace.Controls.svg)](https://www.nuget.org/packages/DataSpace.Controls) | Uno ribbon, navigation, datasheet, object designers, query builders, styles and optional full workspace shell |

Dependencies follow the real project references: `Core ← Query`, `Core ← Storage`, `Core ← Rendering`, `Core ← DataSources ← DataSources.Relational`, and `Core + Query + Storage + Rendering + DataSources ← Controls`.

Only mutate live data through `DatabaseWorkspace.Edit` or `UpdateRecords`. Public models remain mutable for construction/serialization, but direct mutation bypasses history, validation and cache guarantees. Virtual-view records are detached display snapshots, not backing storage. Dispose owned views, renderers and sessions when their host is finished.

### DataSpace.Core

The database document and its transactional workspace: tables with typed fields, records, indexes, relationships with referential checks and cascades, saved queries, forms, reports and macros. Every edit runs against a draft, is validated (required/default/primary/unique constraints, indexes, relationships) and becomes one undoable step. Use it on its own as an embeddable in-memory relational document. No dependencies and no UI.

```sh
dotnet add package DataSpace.Core --prerelease
```

**Key types**

- `DatabaseWorkspace` — current `Document`, `Edit`, `UpdateRecords`, `Undo`/`Redo`, `Replace`, `Changed`.
- `DatabaseDocument`, `TableDefinition`, `FieldDefinition` (`FieldType`), `Record` — the model.
- `RecordOperations` — `Insert`, `Update`, `Delete`, `AddField`, `RenameField` inside an edit.
- `ObjectFactory` — creates tables, queries, forms, reports and macros with unique names.
- `DocumentCodec` — versioned `.dspace` JSON `Serialize`/`Deserialize`; `SchemaValidator` for explicit checks.
- `SampleDatabase.Create` — the Northwind-style demonstration database.

**Usage**

```csharp
using DataSpace.Core;

var workspace = new DatabaseWorkspace(new DatabaseDocument { Name = "Inventory" });
workspace.Edit("Create table", document =>
{
    var table = ObjectFactory.CreateTable(document);   // "Table1" with ID (AutoNumber) and Title
    RecordOperations.AddField(table, new FieldDefinition { Name = "Price", Type = FieldType.Currency });
});
workspace.Edit("Add record", document => RecordOperations.Insert(
    document.Table("Table1"), new Dictionary<string, string?> { ["Title"] = "Notebook", ["Price"] = "4.50" }));

var record = workspace.Document.Table("Table1").Records[0];
workspace.UpdateRecords("Edit title", "Table1", [new RecordEdit(record.Id, "Title", "Drawing pad")]);
workspace.Undo();                                       // Title is "Notebook" again

string json = DocumentCodec.Serialize(workspace.Document);
var reopened = new DatabaseWorkspace(DocumentCodec.Deserialize(json));
```

### DataSpace.Query

A managed SQL engine over a `DatabaseDocument`: streaming SELECT/TOP, joins (with transient hash joins), grouping and aggregates, UNION/UNION ALL, saved queries as sources, parameters, correlated scalar/EXISTS/IN/ANY/ALL subqueries, TRANSFORM crosstabs, and atomic INSERT/UPDATE/DELETE/SELECT INTO action queries through the workspace. It also contains the graphical QBE, crosstab and Find Duplicates/Unmatched designs that round-trip to SQL, and lazy datasheet views. Explicit `QueryOptions` limits bound the work. Depends on `DataSpace.Core`; no UI. See [query analytics](docs/QUERY-ANALYTICS.md) and [subqueries](docs/SUBQUERIES.md).

```sh
dotnet add package DataSpace.Query --prerelease
```

**Key types**

- `QueryEngine` — `Select(document, sql, parameters)`, `Execute(workspace, sql)` for undoable action queries, `IsReadOnly`.
- `QueryResult` / `QueryStatistics` — result `Fields`/`Records`, affected rows, duration and work counters.
- `QueryOptions` — limits and optimizations (subquery cache, hash joins, row caps).
- `TableView.Open` → `VirtualTableView` — lazily materialized, filtered/sorted rows with `ReadPage`.
- `QueryDesign`, `CrosstabDesign`, `FindQueryDesign` — designer models that generate SQL (`ToSql`).

**Usage**

```csharp
using DataSpace.Core;
using DataSpace.Query;

var workspace = new DatabaseWorkspace(SampleDatabase.Create());
var engine = new QueryEngine();

QueryResult sales = engine.Select(workspace.Document,
    "SELECT c.[Company], Count(o.[ID]) AS [Orders] FROM [Customers] AS c " +
    "INNER JOIN [Orders] AS o ON c.[ID] = o.[Customer ID] WHERE c.[Country] = [Country?] GROUP BY c.[Company]",
    new Dictionary<string, object?> { ["Country?"] = "UK" });
foreach (var row in sales.Records)
    Console.WriteLine($"{row["Company"]}: {row["Orders"]}");

var products = TableView.Open(workspace.Document, "Products", sortField: "Product Name");
IReadOnlyList<Record> firstPage = products.ReadPage(0, 50);   // only these rows are materialized

engine.Execute(workspace, "UPDATE [Products] SET [Unit Price] = [Unit Price] * 1.1 WHERE [Category] = 'Beverages'");
workspace.Undo();                                              // action queries are one undo step
```

### DataSpace.Storage

Persistence around the workspace: quoted CSV parsing, import with type inference and export with spreadsheet-formula protection, an optimistic `IWorkspaceStore` contract (compare the expected version and replace atomically) with memory and file adapters, and `WorkspaceSession`, which loads once, tracks the saved snapshot and saves only that snapshot. Depends on `DataSpace.Core`; no UI. The browser app implements the same contract over IndexedDB.

```sh
dotnet add package DataSpace.Storage --prerelease
```

**Key types**

- `CsvCodec` — `Parse`, `Import` (to a `TableDefinition`) and `Export`; `CsvOptions` for delimiter, headers, inference and limits.
- `IWorkspaceStore` / `StoredWorkspace` — version-checked load/save contract.
- `MemoryWorkspaceStore`, `FileWorkspaceStore` — in-memory and atomic file adapters.
- `WorkspaceSession` — `InitializeAsync`, `IsDirty`, `SaveAsync` for a `DatabaseWorkspace`.

**Usage**

```csharp
using DataSpace.Core;
using DataSpace.Storage;

TableDefinition contacts = CsvCodec.Import("Contacts", "Name,Age\nAda,36\nAlan,41\n");  // types inferred
string csv = CsvCodec.Export(contacts.Fields, contacts.Records, new CsvOptions { Delimiter = ';' });

var workspace = new DatabaseWorkspace(new DatabaseDocument { Name = "Contacts" });
using var session = new WorkspaceSession(workspace, new FileWorkspaceStore("data/contacts.store"));
await session.InitializeAsync();                   // replaces the document if one is stored

workspace.Edit("Import contacts", document => document.Tables.Add(contacts));
if (session.IsDirty)
    await session.SaveAsync();                     // fails instead of overwriting a newer version
```

### DataSpace.Rendering

Framework-independent SkiaSharp renderers for the database views: a viewport-only datasheet with selection, a new-record row and cached totals; a relationship diagram; form layouts; report pages shared by preview and PDF export; and Office-style vector icons, all with deterministic hit testing. Use it to draw or export DataSpace objects without Uno. Depends on `DataSpace.Core` and SkiaSharp 4.152; no UI framework.

```sh
dotnet add package DataSpace.Rendering --prerelease
```

**Key types**

- `DatasheetRenderer` + `DatasheetViewState` — `Draw`, `HitTest` (→ `GridHit`), `CellBounds`, content size.
- `RelationshipRenderer` + `RelationshipViewState` — table cards and relationship lines with `HitTest`.
- `FormLayoutRenderer` — form design surface with control hit testing.
- `ReportRenderer` / `ReportPageLayout` — `DrawPage` and `ExportPdf`.
- `OfficeTheme`, `DrawingResources`, `IconRenderer` — colors, cached paints/fonts and icons.

**Usage**

```csharp
using DataSpace.Core;
using DataSpace.Rendering;
using SkiaSharp;

var document = SampleDatabase.Create();
var products = document.Table("Products");

using var datasheet = new DatasheetRenderer();
var state = new DatasheetViewState { ShowTotals = true, ReadOnly = true };
using var surface = SKSurface.Create(new SKImageInfo(900, 400));
datasheet.Draw(surface.Canvas, 900, 400, products.Fields, products.Records, state);
GridHit hit = datasheet.HitTest(products.Fields, products.Records.Count, state, 200, 60);   // Kind, Row, Column

var report = document.Reports.First(r => r.Name == "Customer Directory");
var source = document.Table(report.Source);
using var reports = new ReportRenderer();
File.WriteAllBytes("customer-directory.pdf", reports.ExportPdf(report, source.Fields, source.Records));
```

### DataSpace.Controls

Reusable Office-style Uno Platform controls: ribbon, navigation pane, tab strip and record navigator; the virtualized `DatasheetControl`; table, index, query (SQL and QBE), crosstab, Find, form, report, relationship and macro designers; resource styles; and the optional full `DatabaseWorkspaceView` shell. Individual controls and designers do not require the shell. Depends on Core, Query, Storage, DataSources, Rendering and `SkiaSharp.Views.Uno.WinUI`; requires Uno Platform with the Skia renderer. Merge `ms-appx:///DataSpace.Controls/Themes/OfficeResources.xaml` into your application resources for the supplied styling.

```sh
dotnet add package DataSpace.Controls --prerelease
```

**Key types**

- `DatabaseWorkspaceView` — the complete studio; inject `SaveDatabaseAsync`, `StorageIsDirty`, `ImportTextAsync`, `ExportFileAsync`.
- `DatasheetControl` — `SetData`, `CommitEdits` (`CellEdit`), selection, clipboard, zoom and totals.
- `TableDesignerControl`, `QueryEditorControl`, `QueryDesignerControl`, `RelationshipDesignerControl`, `ReportPreviewControl` — object editors bound to a `DatabaseWorkspace`.
- `OfficeRibbon`, `NavigationPane`, `DocumentTabStrip`, `RecordNavigator` — shell building blocks.

**Usage**

```csharp
using DataSpace.Controls;
using DataSpace.Core;
using DataSpace.Storage;
using Microsoft.UI.Xaml;

var workspace = new DatabaseWorkspace(SampleDatabase.Create());
var session = new WorkspaceSession(workspace, new MemoryWorkspaceStore());
await session.InitializeAsync();
var view = new DatabaseWorkspaceView(workspace)
{
    StorageIsDirty = () => session.IsDirty,
    SaveDatabaseAsync = () => session.SaveAsync()
    // Inject ImportTextAsync and ExportFileAsync for your file-dialog or document-management integration.
};

var window = new Window { Title = "DataSpace", Content = view };
window.Activate();

// A single control without the shell:
var sheet = new DatasheetControl();
var products = workspace.Document.Table("Products");
sheet.SetData(products.Fields, products.Records, readOnly: true);
```

## Performance and verification

Targeted edits avoid whole-database JSON copying, lazy datasheets materialize visible rows, totals are snapshot-cached, and eligible joins use transient hash lookups. Independent subqueries can reuse results within one execution and compatible IN sets use transient membership indexes. [Performance documentation](docs/PERFORMANCE.md) records reproducible managed benchmarks, work counters and remaining limits. Those measurements are not browser/GPU/frame-rate claims.

```bash
dotnet run --project benchmarks/DataSpace.Benchmarks/DataSpace.Benchmarks.csproj \
  -c Release -- artifacts/performance.json
```

`build.yml` runs managed regressions and benchmarks, compiles desktop, publishes WebAssembly, exercises real IndexedDB and browser interactions, packages the libraries and deploys verified `main` builds. PRs never deploy. Exact source, TRX, performance JSON, packages, screenshots and diagnostics are retained as artifacts. See [CONTRIBUTING.md](CONTRIBUTING.md) for browser-test commands.

`release.yml` reruns the full `build.yml` verification, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64), and packs the libraries with symbols. Version tags attach desktop, package, browser and source archives with `SHA256SUMS.txt` to a GitHub Release (prerelease for versions with a suffix) and publish the packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no stored API key) from the protected `nuget` environment. Manual runs take a `version` input and are dry runs: they build and upload every asset as workflow artifacts but publish nothing and do not deploy Pages. Creating a workflow does not imply that a release has been published; check the release history and CI results.

## Save and protect work

**Ctrl+S** commits the active editor and saves locally. Browser data belongs to the origin/profile; clearing site data or losing a profile may remove it. **Export a `.dspace` backup.** Data is not encrypted or uploaded to a cloud database by the storage adapter.

IndexedDB saves compare and replace the expected version inside one transaction. A conflicting tab fails instead of silently overwriting another window; export its work before reloading. The previous committed browser generation is retained, but no recovery-management UI or automatic merge exists. Invalid stored documents are not silently replaced, failed validation preserves editor drafts, and edits made while saving remain dirty.

## License and attribution

MIT licensed. Uno Platform, SkiaSharp and transitive dependency notices remain applicable. No Microsoft Access source, proprietary application icons or Microsoft font files are bundled. Microsoft Access is a Microsoft trademark; DataSpace is independent and not affiliated with or endorsed by Microsoft.
