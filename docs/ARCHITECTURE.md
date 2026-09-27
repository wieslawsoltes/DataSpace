# Architecture

Core owns model, validation, transactions and snapshots. Query and Storage depend on Core. Rendering depends on Core and SkiaSharp, not Uno. Controls combines these libraries as independently usable Uno components. App contains platform entry points, file pickers and browser interop; no reusable library depends on it.

## Transactions and identity

Records have stable internal IDs separate from visible primary keys. Values are canonically represented as strings in document storage. General `DatabaseWorkspace.Edit` deep-copies the model, applies mutations, validates the candidate, then publishes one undoable revision. Failed edits retain the live document and history.

`UpdateRecords` is the controlled copy-on-write path for cell/range edits. Metadata and record-list containers are copied, unchanged record dictionaries are shared, and only modified rows are cloned. Targeted constraint checks never normalize or mutate shared rows. Cascading parent-key changes use a fully detached fallback. A subsequent generic edit deep-copies shared rows before invoking external mutation code. Undo/redo deep-copy their restored snapshot and retain monotonic revisions.

Public model objects are mutable to support serialization/construction. Consumers must not mutate the live document or old snapshots directly; doing so bypasses all transaction and cache guarantees. This is a single-writer workspace, not a concurrently mutable database server.

`TableView.Open` returns a snapshot-backed lazy record list with a bounded 256-record display cache. Identity lookup can inspect backing IDs without enumerating/cloning visible records. Filtering/sorting stores an index order; native cells receive detached records on demand. General SQL projections and joins remain read-only because they are not necessarily updatable. See [performance](PERFORMANCE.md) for exact work and memory boundaries.

## SQL and visual design

SQL is tokenized into managed AST plans, not passed to JavaScript eval or a shell. SELECT streams ordinary projections/filters. Single-table streaming scans reuse their scalar context; reference buffered groups and join inputs keep distinct contexts. GROUP BY uses accumulators per group, while ordered TOP/LIMIT retains at most offset+limit sort candidates. Unbounded ORDER BY still materializes and sorts a bounded buffer. Eligible qualified equality joins build transient hash buckets, probe candidate rows and still apply the full ON predicate. Other joins retain a work-limited nested-loop path. Stats expose reads, join choices and candidate counts.

UNION/ALL and TABLE branches use matching column counts, first-branch column names, type normalization and explicit duplicate semantics. Saved read-only queries can serve as sources; recursion is cycle/depth checked and parameters compose with explicit overrides. INSERT SELECT materializes before inserting, including self-appends, inside an atomic workspace transaction.

`QueryDesign` is independent of Uno/Skia. It represents SELECT sources, joins, projected columns, sort order, aggregate choices and AND/OR criteria. SQL remains authoritative: `Restore` accepts serialized layout only when generated SQL agrees with the supplied SQL. Unsupported or malformed state falls back to parsing SQL. Unsupported compound/action SQL remains in SQL View rather than being overwritten by an approximate diagram.

`CrosstabDesign` and `CrosstabBuilderControl` add a separate single-table authoring flow. The TRANSFORM executor shares aggregate accumulators with GROUP BY and stores sparse group/pivot cells. SELECT INTO and index DDL use validated workspace transactions; make-table copies direct column types/sizes without copying source constraints. See [query analytics](QUERY-ANALYTICS.md).

The native Uno QBE grid and draggable source cards edit this detached model. Table, index, form, report and macro editors likewise retain drafts until validation succeeds. Field/table SQL dependency rewriting is still conservative, not a full dependency graph.

## Rendering and lifetime

Renderers consume SKCanvas and logical units; they can be used independently for offscreen/native rendering. `SkiaSurface` uses the shared Uno canvas where available and a DPI-correct SKXamlCanvas fallback otherwise. Native Uno TextBox controls provide text editing. Datasheets draw visible cells and cache aggregate totals against record snapshots. The host chooses backend/hardware behavior; hardware-GPU performance is not implied by headless CI.

## Persistence

`IWorkspaceStore` loads serialized content with an opaque version and atomically saves against an expected version. `WorkspaceSession` serializes storage operations and tracks the exact saved snapshot, so edits during I/O remain dirty. Initialization validates before replacing data and rejects concurrent edits or corrupt content rather than discarding them.

Browser IndexedDB compares and writes in one read-write transaction across tabs, retaining one previous generation. This supplies conflict detection, not collaborative merging, authentication or an audit log. The host imports browser modules relative to document.baseURI for repository-subpath deployment.

## Dependency and verification boundaries

The single-project Uno application has desktop and browser targets. A version-scoped build target adapts Uno 6.7's unmodified C GL shim to SkiaSharp 4.152.1's C++20 link invocation using an intermediate extern-C wrapper. It does not modify NuGet caches or substitute a graphics implementation; review/remove it when upgrading to a compatible dependency combination.

CI tests engine semantics, optimized/reference result equivalence, bounded materialization and rollback, then publishes the real browser app and exercises native UI interactions/IndexedDB. It also packages all five libraries and records reproducible benchmarks. These checks do not establish full Access compatibility, native OS behavior, all accessibility/security requirements, production scaling or independent downstream package-consumer qualification.
