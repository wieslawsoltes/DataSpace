# DataSpace

**A modular database studio for the browser and desktop, built with Uno Platform and SkiaSharp.**

[![Build, test and deploy](https://github.com/wieslawsoltes/DataSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/DataSpace/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[Browser application](https://wieslawsoltes.github.io/DataSpace/) · [Releases](https://github.com/wieslawsoltes/DataSpace/releases) · [Architecture](docs/ARCHITECTURE.md) · [Compatibility](docs/COMPATIBILITY.md) · [Contributing](CONTRIBUTING.md)

DataSpace brings an Access-style ribbon, navigation pane, tabbed objects, editable datasheets, SQL queries, form design, report previews and relationship diagrams to a shared .NET codebase. The browser application is the actual Uno/Skia application compiled to WebAssembly—not an HTML mockup or a different front end.

> **Status: early implementation.** The familiar layout is an independent implementation, not verified pixel-for-pixel Microsoft Access parity. Native `.accdb`/`.mdb`, ACE/Jet, VBA, linked server databases and the complete Access feature set are not supported. Read the [compatibility matrix](docs/COMPATIBILITY.md) before using existing database files or planning a migration.

## Workspace

| Area | Implemented behavior |
| --- | --- |
| Application shell | Access-style red title bar, quick-access commands, grouped ribbon, File backstage, searchable object navigation, document tabs, record navigation and status/error surfaces. |
| Tables | Typed fields, record insert/update/delete, primary and unique constraints, required values, field defaults, transactional undo/redo, detached table-design drafts and field property editing. |
| Datasheets | Skia-drawn visible cells, row/range selection, keyboard navigation, in-place native text editing, TSV clipboard, sortable/resizable columns, filters, search and totals. Sorted and filtered edits preserve source record identities. |
| Queries | Managed SQL parser/evaluator, SQL and parameter editing, explicit SELECT builder, read-only result grids, aggregation and joins within the supported dialect, confirmed atomic action queries. |
| Forms | Bound record entry, text and Yes/No controls, record navigation, visual layout selection, drag/resize with grid snapping, control creation/deletion, geometry and caption properties. |
| Reports | Table or saved-query sources, field selection, title/orientation editing, pagination, zoom and Skia PDF export using the same page renderer. |
| Relationships | Draggable table cards, relationship creation/removal, referential integrity and configured cascading changes. |
| Macros | Ordered, explicitly invoked actions for opening objects, filtering, record navigation and saving. No imported macro automatically executes. |
| Files and persistence | Versioned `.dspace` JSON documents, CSV import/export, local desktop files, transactional IndexedDB browser storage, cross-tab optimistic conflict detection and explicit file backups. |

The initial workspace contains a small Northwind-style demonstration database. Its records are sample data. Use **File → New** for an empty database or **External Data → Text File** to import CSV.

## Run and build

The repository pins **Uno.Sdk 6.7.30**, **SkiaSharp 4.152.1** and the **.NET 10** SDK family. Managed and native Skia packages are aligned with `SkiaSharpVersion`; changing only one package can cause runtime or restore failures.

```bash
git clone https://github.com/wieslawsoltes/DataSpace.git
cd DataSpace
dotnet workload install wasm-tools

# Managed database/query/storage regression suite
dotnet test tests/DataSpace.Tests/DataSpace.Tests.csproj -c Release

# Desktop application: choose the backend for the current operating system
dotnet run --project src/DataSpace.App/DataSpace.App.csproj -f net10.0-desktop

# Build the actual browser application
dotnet publish src/DataSpace.App/DataSpace.App.csproj \
  -c Release -f net10.0-browserwasm -o artifacts/publish
python3 scripts/stage-site.py
python3 -m http.server 8080 --directory artifacts/site
```

Open `http://localhost:8080/`. Do not open `index.html` through `file://`: the WebAssembly runtime and module imports require an HTTP(S) origin. A desktop display server and the native dependencies of the selected Uno backend are required to run the desktop host. The CI pipeline separately compiles the desktop host and runs the browser host in headless Chromium.

For browser checks using the same project subpath as GitHub Pages, see [CONTRIBUTING.md](CONTRIBUTING.md). The site uses a relative application base path, and the standard build does not require cross-origin-isolation headers or WebAssembly threads.

## Independently reusable libraries

| Package/project | Responsibility | Depends on the app? |
| --- | --- | --- |
| `DataSpace.Core` | Document model, typed values, schema/relationship validation, record operations, schema drafts, object factories, transaction history and JSON codec. | No |
| `DataSpace.Query` | Managed SQL, scalar expressions and identity-preserving editable table views. | No |
| `DataSpace.Storage` | CSV, optimistic stores and asynchronous save-session coordination. | No |
| `DataSpace.Rendering` | Skia datasheet, form, relationship, report and icon renderers. | No |
| `DataSpace.Controls` | Reusable Uno controls, resource styles, object editors and the complete optional workspace shell. | No |
| `DataSpace.App` | Thin desktop/WebAssembly entry points, platform file pickers and browser interop. | Application host |

All five libraries can be built and packaged separately. A release workflow creates `.nupkg` files and a browser distribution as GitHub Release assets. It does **not** automatically publish to NuGet.org. Referencing a library project directly is also supported.

### Use the engine without Uno

```csharp
using DataSpace.Core;
using DataSpace.Query;

var workspace = new DatabaseWorkspace(new DatabaseDocument { Name = "Inventory" });
workspace.Edit("Create table", document => ObjectFactory.CreateTable(document));
workspace.Edit("Add record", document => RecordOperations.Insert(
    document.Table("Table1"),
    new Dictionary<string, string?> { ["Title"] = "Notebook" }));

var result = new QueryEngine().Select(workspace.Document,
    "SELECT ID, Title FROM Table1 ORDER BY Title");

// Editable table views retain the identity of the original records.
var view = TableView.Select(workspace.Document, "Table1", sortField: "Title");
workspace.Undo();
```

Edit models through `DatabaseWorkspace.Edit`, not by changing `Workspace.Document` directly. Transactions validate a detached copy and only replace the live document on success. The public model is mutable for serialization and construction; it is not an immutable collection API.

### Host the controls in another Uno application

```csharp
using DataSpace.Controls;
using DataSpace.Core;
using DataSpace.Storage;

var workspace = new DatabaseWorkspace(SampleDatabase.Create());
var session = new WorkspaceSession(workspace, new MemoryWorkspaceStore());
await session.InitializeAsync();
var view = new DatabaseWorkspaceView(workspace)
{
    StorageIsDirty = () => session.IsDirty,
    SaveDatabaseAsync = () => session.SaveAsync()
};
// Place view in your window/page. Inject ImportTextAsync and ExportFileAsync
// for your own file-dialog or document-management integration.
```

Merge `ms-appx:///DataSpace.Controls/Themes/OfficeResources.xaml` into the host application's resources to use the supplied Office-style component templates. Individual controls such as `DatasheetControl`, `OfficeRibbon`, `TableDesignerControl` and `FormEditorControl` do not require the complete shell.

## Saving and protecting work

**Ctrl+S / Save** commits the active editor and saves the current document locally. In the browser, local data belongs to the current origin and browser profile. Clearing site data, using a private session or losing that profile may remove it. **Export a `.dspace` file for a separate backup.** Storage is not encrypted and is not a cloud service.

Browser saves compare the expected stored version and replace the data in one IndexedDB transaction. A competing tab produces a conflict instead of silently overwriting another tab. Export the losing tab's work before reloading. Saves retain the prior committed browser generation, but a recovery-management UI is not yet implemented.

Invalid stored documents are not silently replaced. Failed validation retains editor drafts. Edits made while a save is in progress remain dirty after the older snapshot finishes saving. Undo/redo is bounded in-memory history; it is not a persistent audit log.

## Automation and releases

`build.yml` tests the managed engine, compiles the desktop host, publishes the browser host, checks real IndexedDB behavior in two browser tabs, launches the real Uno application, captures screenshots and publishes the verified static artifact to GitHub Pages on `main`. Pull requests never deploy. Browser diagnostics and test results are retained as workflow artifacts.

`release.yml` runs the same verification on version tags, packages the reusable libraries and browser distribution, creates checksums and attaches them to a GitHub Release. Tag naming is `vMAJOR.MINOR.PATCH` with an optional prerelease suffix.

GitHub Pages must be enabled with **GitHub Actions** as the deployment source. The workflow attempts automatic enablement, but an organization or repository policy can require an administrator to enable it first. Check the deployment job rather than assuming that a successful compile means the public site is live.

## License and attribution

DataSpace source is MIT licensed. The project uses Uno Platform and SkiaSharp; their notices and the notices of their transitive dependencies remain applicable. No Microsoft Access source code, proprietary icons, database engine or Microsoft font files are bundled. Microsoft Access is a trademark of Microsoft. DataSpace is independent and is not affiliated with or endorsed by Microsoft.
