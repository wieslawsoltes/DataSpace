# Access-style UI coverage

DataSpace is an independent Uno/Skia implementation, not complete or pixel-exact Microsoft Access. Commands in the new contextual tabs invoke existing editors and engine operations; unavailable Access features are not represented as decorative working buttons.

## Implemented in preview.7

**Contextual ribbon:** Table Fields in table datasheets; Table Design with insert/delete/move fields, primary key, indexes and property sheet; Query Design with view switching, Run, builders and export; Form/Form Design with view switching, existing label/heading/delete actions and property sheet; Print Preview with page navigation, portrait/landscape, zoom and PDF export. Existing editor toolbars remain available for standalone embedding. Context tabs update when objects change, not on every data edit. F4 toggles table/form property sheets in design view.

**Saved table datasheets:** Hide Fields, Unhide Fields, Freeze Fields, Unfreeze All Fields, Column Width, Best Fit and Datasheet Formatting are accessible through Table Fields and the datasheet context menu. The layout dialog reorders fields and edits visibility/freeze, uniform row height, font size/bold, alternate row shading and horizontal/vertical gridlines. Click a header to select a whole column; Shift-click extends across adjacent headers. Ctrl+Space selects the current column and Shift+Left/Right extends the range. Right-click inside a range retains it; Hide/Freeze apply to the full range in one undoable layout edit. The header arrow and sorting commands remain available for sorting. Frozen fields move left; unfreezing preserves that displayed order. Selecting columns never enumerates all their rows, and Delete while columns are selected does not delete records. Hiding every remaining field is rejected atomically. Best Fit measures the heading and first 200 rows rather than scanning the entire table. Layout changes affect presentation only, are undoable, and persist in `.dspace` and local saves. Existing documents receive default settings. Field renames preserve their presentation settings; obsolete names after removals are ignored and new fields appear at the end. At least one field remains visible. Formatting is currently single-line text, not full rich text/wrapped-cell/conditional formatting. Query/form result layouts are not persisted by this table setting. Native cell editors are clipped to the visible cell area so they cannot cover frozen columns, headers or totals; fully occluded cells require unfreezing columns or a wider viewport before editing.

Metadata changes share old record dictionaries and validate only layout/width settings, rather than normalizing all data. Container lists are still copied. The benchmark reports this managed transaction separately from setup, rendering, storage or undo costs.

**Find and Replace:** Home → Find/Replace or Ctrl+F/Ctrl+H opens a reusable native dialog. Choose one displayed field or all displayed fields, case sensitivity, any/whole/start-of-field matching, forward/backward direction and raw/formatted search. Search wraps once in the current filtered view. Text is literal (no wildcard/regular-expression syntax). Null does not match and AutoNumber is never replaced. Replacement is permitted on raw values only and still obeys destination type/constraint rules. Replace All changes its button to Confirm Replace All and applies no change until that second action. A failed type/unique/relationship check leaves all data and history unchanged. Cell, expansion and cumulative character budgets apply. Field-scoped search resolves its column once instead of traversing every displayed column for each record; an all-field scan reuses the current lazy display row. Unchanged replacement matches consume neither edit nor character budgets. Search/replace and large managed queries are not yet asynchronously scheduled in the browser.

**Navigation and documents:** The navigation header menu chooses an object-type filter, ascending/descending name sort, expanded/collapsed groups, session-only hidden-object visibility and single/double click behavior. Search is debounced. The pane's right edge can be dragged between 160 and 480 logical pixels; F11 or Help → Navigation Pane toggles it. Document context menus open/design/close an object, close other objects or close all. Ctrl+Tab / Ctrl+Shift+Tab cycle documents; Ctrl+F4 or Ctrl+W closes the current object where the browser/OS delivers the shortcut. Help commands provide alternatives to browser-reserved keys. Closing or switching commits valid drafts first; failed validation keeps the active editor. Navigation hiding is presentation, not authorization. Object sorting/hiding/filter/click/width preferences last for the current workspace session only.

## Remaining UI families

| Family | Material gaps |
| --- | --- |
| Application shell | Complete Access Options/Trust Center, ribbon/Quick Access customization, XML ribbons, Alt KeyTips, native window modes, complete Backstage/print dialogs, startup templates/recent-file experience. |
| Navigation | User-defined category/group trees, created/modified-date and detailed/icon views, persisted preferences and complete object-property dialogs. |
| Datasheets/tables | Header drag-reordering, subdatasheets, lookup/multivalue/attachment/OLE/calculated controls, wrapped/rich text, color/font-family/conditional formatting galleries and complete validation/input-mask UI. |
| Queries | Complete visual action/UNION/nested-query design, expression builder, pass-through connections, every query wizard and parameter-declaration UI. |
| Forms | Full control toolbox, subforms, tab controls, combo/list binding, option groups, multi-selection/alignment/distribution, control layouts, tab order, event/property pages and full Access event runtime. |
| Reports/printing | Sections/grouping/sorting/totals designer, conditional formatting, subreports, labels/chart/report wizards, page setup/print device parity and full report layout editing. |
| External data/tools | Persistent import/export specifications/jobs, linked-table manager, every ODBC/OLE DB/SharePoint/Excel/XML source dialog, database documenter/analyzer/splitter/repair tools and complete macro/VBA modules. |
| Qualification | Pixel comparison with a specific Access version/theme/DPI, every browser/OS, accessibility, touch, localization/RTL, native fonts, high-DPI and hardware-GPU behavior. |

## Reference behavior

The implementation is informed by Microsoft's documentation rather than bundled Microsoft assets:

- [Guide to the Access UI](https://support.microsoft.com/en-us/access/guide-to-the-access-user-interface)
- [Working with datasheets](https://support.microsoft.com/en-us/access/working-with-datasheets)
- [Freeze fields](https://support.microsoft.com/en-us/access/freeze-fields-in-an-access-datasheet)
- [Show/hide columns](https://support.microsoft.com/en-us/access/show-or-hide-columns-in-a-datasheet)
- [Navigation Pane](https://support.microsoft.com/en-us/access/use-the-navigation-pane)

DataSpace deliberately uses transactional layout/record undo and a two-stage Replace All confirmation; those are not assertions of identical Access behavior. No Microsoft source, proprietary icons or Microsoft font files are included.

## Regression scope

The browser suite exercises saved table layouts, rejected dimensions, undo/redo, raw Replace All, navigation filtering, tab closing/cycling and actual contextual query/form/report commands. Form controls are added/deleted through the ribbon and their saved model changes are asserted; report navigation, orientation and zoom are checked separately. These scenarios do not establish all UI-family, native platform or pixel parity. Managed geometry tests check native-editor bounds under frozen columns, scrolling, totals and zoom without hardware-GPU qualification. Search benchmarks compare the former flattened cell traversal with one-time field resolution; both read the same requested field values. Setup, rendering and storage are excluded.

Whole-column regressions exercise forward/reverse ranges, keyboard extension, right-click range retention, group hide/freeze, refusal to hide every field or delete all rows, undo/redo and save/reload. CPU Skia raster tests verify header/row highlighting at 1× and 2× without selecting the new-record placeholder. This does not implement header drag reordering or nonadjacent column selection.
