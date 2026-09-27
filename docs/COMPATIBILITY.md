# Compatibility and remaining work

DataSpace follows familiar Access interaction patterns but is not a drop-in replacement for Microsoft Access. This document describes the implementation boundary, not a promise that unimplemented features are present.

| Area | Current support | Material remaining work |
| --- | --- | --- |
| UI fidelity | Ribbon groups, red title bar, navigation pane, document tabs, property editors, datasheet, File backstage. | Verified pixel matching, complete contextual ribbons, command parity, touch/accessibility review, native font consistency and all dialogs/wizards. |
| Storage formats | Versioned JSON `.dspace`, bounded CSV import/export. | Native ACCDB/MDB reading/writing, Jet/ACE semantics, legacy format fidelity, password/encryption compatibility, attachments and OLE fields. |
| Tables | Typed values, required/default/unique/primary constraints, fields, indexes in the core model, record operations. | Complete Access field types and property semantics, lookup/multivalue/calculated fields, full index designer, validation-expression UI and datasheet subrecords. |
| Transactions | Validated copy-on-write edits, bounded undo/redo, revision guards, optimistic store writes. | Page-level storage, durable journaling, persistent transactional indexes, incremental history and database-scale performance qualification. |
| SQL | SELECT, projections, supported expressions, INNER/LEFT joins, grouping/HAVING, ordering, DISTINCT, TOP/LIMIT/OFFSET, parameters; supported INSERT/UPDATE/DELETE and table DDL. | Complete Access SQL, TRANSFORM/crosstabs, UNION/subquery coverage, pass-through queries, optimizer/index planning, full dependency rewriting and updatable joined queries. Unsupported syntax must fail explicitly. |
| Query designer | SQL/parameter editor, explicit simple SELECT builder, read-only result grid. | Access graphical join/QBE design surface, bidirectional diagram/SQL translation, expression builder and all query wizards. |
| Forms | Table-bound text and Yes/No inputs, record navigation, layout drag/resize and property sheet. | Full control library, subforms, list/combo lookup binding, events, tab order, responsive/anchored layouts, complete validation and control-source expressions. |
| Reports | Table/query source, columns, title, landscape, fixed-layout pagination and Skia PDF export. | Group sections, conditional formatting, calculated fields, subreports, charts, rich text, interactive print dialogs and advanced total layouts. The model's ShowTotals property is not a complete report-total implementation. |
| Relationships | Core referential validation, cascade behavior, draggable diagrams and authoring. | Composite relationship designer, full Access cardinality/display semantics, relationship editing wizards and backend enforcement outside the local document. |
| Automation | Explicit bounded macro steps for workspace actions. | VBA, COM/ActiveX, modules, Access event lifecycle, data macros and complete macro actions. No arbitrary imported code auto-runs. |
| External data | CSV and DataSpace file dialogs. | ODBC/OLE DB, SQL Server, SharePoint, linked tables, live synchronization and authenticated connectors. |
| Multi-user | Cross-tab optimistic save conflict detection. | Real-time collaboration, merge UI, server-side authorization, role administration, durable activity history and enterprise operations. |
| Deployment | Uno desktop host, WebAssembly host, static Pages pipeline, package/release workflow. | Signed native installers, complete OS/browser certification, offline-update lifecycle and app-store distributions. |
| Security/accessibility | Validation, operation limits, conservative conflict handling, native controls for text input. | Independent security assessment, comprehensive untrusted-input fuzzing, per-cell accessibility, complete keyboard/accessibility testing and production hardening. |

## File and data precautions

Do not rename an Access file to `.dspace`; this does not convert it. Export supported data to CSV from its original application, then import that data. Relationships, formulas, forms, reports and macros are not preserved by CSV.

Browser data is local, unencrypted and scoped to the origin and browser profile. Keep exported `.dspace` backups. A cleared browser profile can remove local data. Desktop close/exit behavior and OS file-dialog integration require platform testing; do not assume the browser's unload warning is a complete native unsaved-work guard.

A failed save keeps the session dirty. A version conflict requires preserving/exporting local work before reloading; there is no automatic record-level merge. Report PDFs are basic document exports, not certified archival or accessible PDF output.

## Visual parity

The UI is an independently drawn and styled Access-like workspace. Microsoft proprietary fonts and application assets are not included. Font availability, system scaling and backend rendering can alter geometry. Browser startup screenshots are useful diagnostics; they are not a pixel-comparison qualification against Microsoft Access.
