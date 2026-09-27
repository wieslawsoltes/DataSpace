# Architecture

## Dependency boundaries

```text
DataSpace.App ──→ DataSpace.Controls ──→ DataSpace.Rendering ──→ SkiaSharp
                       │                        │
                       ├──→ DataSpace.Query ────┤
                       ├──→ DataSpace.Storage ──┤
                       └────────────────────────┴──→ DataSpace.Core
```

Core does not reference Uno, SkiaSharp, a browser or the application. Query and Storage depend on Core. Rendering depends on Core and SkiaSharp, not Uno. Controls combines those layers in reusable Uno components. Platform-specific concerns are confined to the app host, except for the independently usable browser storage module.

## Document transaction model

A database document owns tables, records, indexes, relationships, queries, forms, reports and macros. A record has a stable internal identity separate from its visible primary-key value. Typed field values have a canonical string representation in the serialized document.

`DatabaseWorkspace.Edit` clones the current document, invokes the mutation, validates the candidate and publishes it only if all work succeeds. One user operation is one undo entry. Failed conversion, uniqueness, required-field or referential-integrity checks leave the live document and history unchanged. Optional expected revisions prevent a stale detached editor from overwriting a newer document.

The controls deliberately stage table schemas, form geometry, report properties and macro steps. Navigation first asks the active editor to commit. A validation failure preserves the view and the draft instead of dismissing it. General SELECT results are read-only because projections and joins are not necessarily updatable. `TableView.Select` separately preserves source record identities for editable sorted/filtered datasheets; it returns detached rows and field metadata so a drag preview cannot mutate the document outside a transaction.

Public model objects are mutable. Consumers must use the workspace transaction API for edits; direct mutation bypasses history, revision and dirty-state guarantees. Full-document copying favors understandable atomic behavior over large-database scalability. Persistent indexes, incremental transactions, background query scheduling and production-scale memory qualification are future work.

## Query execution

SQL is tokenized and parsed into internal plans; it is not passed to JavaScript `eval`, an operating-system shell or a remote server. Scalar evaluation, null handling, grouping and typed comparisons run in managed code. Query options bound intermediate and result sizes, and execution accepts cancellation tokens. Action statements are applied inside a workspace transaction. The UI additionally asks for confirmation before an action query runs.

Saved SQL dependency rewriting is intentionally conservative: table/field renames and drops are blocked while saved queries exist rather than editing arbitrary SQL substrings. This is a safety restriction, not a complete SQL dependency graph. The SELECT builder explicitly replaces its SQL text when Generate is pressed; it is not a bidirectional graphical Access query designer.

## Rendering and input

Renderers accept `SKCanvas`, logical dimensions, model data and small view-state objects. They can be used outside Uno for offscreen rendering. The Uno `SkiaSurface` chooses the shared `SKCanvasElement` path when supported and otherwise uses a DPI-scaled `SKXamlCanvas`. Drawing state is restored after each paint; pointer coordinates remain in logical units. The host supplies the platform renderer; physical GPU behavior depends on Uno, the operating system and the browser.

The datasheet draws visible rows and columns, and uses a native Uno text input for editing rather than simulating a text caret in pixels. Forms use native text/checkbox controls in record view and Skia drawing in design view. Report preview and PDF export share the page renderer. Screen-reader semantics for individual canvas cells and comprehensive accessibility qualification remain incomplete.

## Persistence and concurrency

`IWorkspaceStore` is a two-method optimistic-storage contract. Load returns serialized content and an opaque version. Save must compare the expected version atomically and return the committed version. Memory and file stores support tests and desktop hosts; IndexedDB supplies browser persistence.

`WorkspaceSession` serializes storage operations. It records the exact document reference successfully saved, so an edit during asynchronous I/O remains dirty. Initialization deserializes and validates before replacing the workspace and rejects a concurrent edit rather than discarding it. A failed initialization leaves saving disabled until successful initialization; exporting an independent copy remains possible.

`IndexedWorkspaceStore` compares and writes inside one `readwrite` transaction, including when different tabs hold different connections. It keeps one previous envelope. Corrupt envelopes, stale versions and failed writes reject without marking the session clean. This is conflict detection, not live collaboration, authentication, access control or an audit log.

## Hosting and build compatibility

The single-project Uno application has desktop and WebAssembly entry points. The browser imports an ES module by an absolute URL derived from `document.baseURI`, so it works under the GitHub Pages repository subpath. No application data is uploaded to GitHub by this storage adapter.

`Directory.Build.targets` contains a narrowly scoped Uno 6.7 / SkiaSharp 4.152.1 build adaptation. SkiaSharp's WebGPU bridge adds C++20 linker flags while Uno supplies a raw C GL shim. The target copies that installed shim unchanged into the intermediate directory and includes it from a C++ translation unit under `extern "C"`. The adaptation retains C exports, changes no NuGet cache files and does not substitute a different graphics implementation. Re-evaluate and remove it when upgrading to a dependency combination that resolves the mixed-language link command.

## Validation boundaries

Managed regression tests cover database constraints, rollback, history, SQL, CSV, storage conflicts, save races, schema drafts and editable row identity. Headless browser checks use real IndexedDB and start the actual compiled Uno application. They do not establish full interaction parity, pixel equality, native OS behavior, assistive-technology support, hardware-GPU performance, or safe handling of every adversarial file. Read the compatibility matrix and recorded CI results separately.
