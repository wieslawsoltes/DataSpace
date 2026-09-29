# Compatibility and remaining work

DataSpace 0.2.0-preview.4 is an independent Access-style application, not a drop-in Microsoft Access replacement. The table distinguishes implemented workflows from remaining compatibility boundaries.

| Area | Implemented | Material remaining work |
| --- | --- | --- |
| UI | Ribbon, quick access, File backstage, navigation, tabs, datasheets, native editors and property sheets. | Verified pixel matching, full contextual ribbons, every command/dialog/wizard, complete keyboard/accessibility/touch qualification. |
| File formats | Versioned JSON `.dspace`, CSV, JSON and text-preserving SQLite import/export. | Native ACCDB/MDB reading/writing, Jet/ACE storage semantics, encryption, attachments and OLE. |
| Tables | Typed values, required/default/unique/primary constraints, field designer, ordered single/composite index designer with validation and undo. | Full Access field types, lookup/multivalue/calculated fields, field validation expressions, composite primary-key semantics and subdatasheets. |
| Transactions | Atomic validation/rollback, targeted copy-on-write record edits, revision checks, bounded undo/redo and optimistic saves. Action subqueries read pre-statement data. | Durable journals, page-level storage, persistent indexes, incremental history and enterprise-scale qualification. |
| SQL | SELECT expressions, INNER/LEFT/CROSS joins, GROUP BY/HAVING, ORDER BY, DISTINCT, TOP/LIMIT/OFFSET; UNION/UNION ALL and TABLE branches; saved sources; scalar/EXISTS/IN/ANY/SOME/ALL SELECT subqueries with lexical correlation; INSERT VALUES/SELECT, UPDATE/DELETE, SELECT INTO, constraint-index DDL and TRANSFORM/PIVOT. | Derived FROM subqueries, RIGHT/FULL joins, pass-through SQL, complete Access functions/coercion/collation, PARAMETERS declarations, complete index DDL, a cost-based optimizer and updatable joined queries. |
| Query design | QBE fields/aliases/show/sort/totals/criteria/OR, source cards, join authoring, SELECT SQL round trips, crosstab builder, Find Duplicates/Unmatched table-query builders. | Graphical editing of nested SELECT internals, UNION/action queries and advanced crosstabs, remaining query wizards, complete expression builder and full SQL dependency rewriting. Unsupported designs remain in SQL View. |
| Forms | Bound text/YesNo controls, record navigation, snapped layout drag/resize and property sheet. | Subforms, complete list/combo/control library, events/VBA, tab order, responsive layouts and control-source expressions. |
| Reports | Table/query source, title/orientation/field selection, pagination, zoom and Skia PDF export. | Group sections, calculated expressions, subreports, conditional formatting, charts, full totals and production print workflows. `ShowTotals` is not complete report-total support. |
| Relationships | Referential checks/cascades, draggable diagrams and authoring. | Composite relationship designer, backend enforcement and all Access cardinality/display semantics. |
| Automation | Explicit bounded macro actions for opening objects, filtering, navigation and saves. | VBA/COM/ActiveX, modules, full Access event lifecycle, data macros and all macro actions. Imported macros never auto-run. |
| External data | JSON files/URLs/Pointer selection, SQLite files, paged gateway sources for PostgreSQL/MySQL/MariaDB/SQL Server. | General ODBC/OLE DB, SharePoint, live linked tables, write-through/federated SQL and per-user source authorization. |
| Collaboration | Atomic cross-tab version conflict detection in IndexedDB. | Live collaboration, merge/recovery management UI, server authentication/authorization and durable audit history. |
| Performance | Indexed JSON page access, bounded source-page cache, SQLite browser worker, lazy rows, cached totals, debounced search, targeted edits, streaming groups/scans, bounded TOP, transient hash joins, per-execution independent/repeated-correlation subquery caches and compatible IN membership sets. | Persistent page/index engine, decorrelation of arbitrary correlated subqueries, asynchronous/cooperative browser execution, full-scale/hardware-GPU and storage-I/O qualification. See [measurement scope](PERFORMANCE.md). |
| Distribution | Uno desktop/browser hosts, seven reusable packages, package-only consumer compilation on both heads, Pages and version-tag release workflows. | Signed installers, complete OS/browser certification and independent third-party runtime qualification. |

See [query analytics](QUERY-ANALYTICS.md) for crosstab/make-table/index syntax and [subqueries and Find builders](SUBQUERIES.md) for nested-query scope, null behavior, action snapshots and execution budgets. These are supported managed workflows, not full ACE/Jet equivalence.

## SQL boundaries

UNION branches require the same number of columns; output names come from the first branch and ORDER BY belongs after the final branch. UNION ALL preserves duplicates, while UNION removes duplicate tuples. TOP/LIMIT/OFFSET belong to individual SELECT branches. FIRST/LAST retain the engine's documented non-null behavior.

Saved sources are read-only and cycle/depth checked. Default parameters can be overridden by the calling query. Nested scalar/IN/quantified queries require one column. Scalar results fail on multiple rows. Derived FROM tables and nested TRANSFORM syntax are not implemented. Standalone ExpressionEvaluator and table-view filters do not execute subqueries. The Find builders generate ordinary SQL without a separate restorable wizard state; single-key duplicate details use grouped membership and an explicit null count; composite details retain correlated counts with bounded repeated-key reuse. Cache and query work limits still apply.

Compound/action SQL is not translated into the SELECT-only visual designer. Nested SELECT internals remain expressions rather than separate graphical diagrams. Field/table renames remain conservative while saved SQL exists; a complete dependency graph is not implemented.

## Data precautions

Renaming `.accdb` or `.mdb` to `.dspace` is not conversion. CSV migration transfers records only, not forms, relationships, macros or queries. Browser storage is local, unencrypted and scoped to the origin/profile; keep exported `.dspace` backups. Conflicting saves require exporting local work before reloading. Native close/exit behavior, independent security assessment, adversarial input fuzzing and comprehensive assistive-technology support remain unqualified.

The UI is independently styled and drawn. No Microsoft Access source, proprietary application assets or Microsoft font files are bundled. Font availability and platform scaling affect appearance; startup screenshots are not a pixel-parity certification.

## External-source extension (preview.4)

JSON files/URLs and JSON Pointer selection, real SQLite files on browser/desktop, and a read-only HTTP gateway for PostgreSQL, MySQL, MariaDB, SQL Server and SQLite are implemented. The gateway uses configured table allowlists, parameterized page limits, an access token, exact CORS origins, concurrency/time/response bounds and redacted errors. This is **not** complete ODBC/OLE DB, persistent linked tables, federated query planning or write-through editing. Imports are local copies; refresh updates the session preview, not an already imported table.

The source layer defaults to conservative text preservation for relational values. SQLite exports use TEXT/null columns and do not preserve original types, indexes, triggers, views, foreign keys or Access objects. SQLite opening is capped at 16 MiB; external imports at 100,000 rows; source tables at 128 columns. Multi-page remote reads are not a cross-request transaction: choose a stable unique ordering key and avoid concurrent source changes when importing. A shared gateway token grants access to all configured sources; this is not per-user authorization. TLS, deployment secrets and least-privilege database permissions are the operator's responsibility. Independent security and production-scale qualification remain open.
