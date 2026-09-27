namespace DataSpace.Controls;

/// <summary>The Access-style workspace command layout; applications may replace this catalog.</summary>
public static class OfficeCommandCatalog
{
    private static RibbonCommand C(string id, string label, string icon, bool large = false, string? shortcut = null) => new(id, label, icon, large, shortcut);
    public static IReadOnlyList<RibbonTab> Create() =>
    [
        new("file", "File", []),
        new("home", "Home", [
            new("Views", [C("view", "View", "table", true)]),
            new("Clipboard", [C("paste", "Paste", "paste", true, "Ctrl+V"), C("copy", "Copy", "copy", shortcut: "Ctrl+C")]),
            new("Sort & Filter", [C("ascending", "Ascending", "sort"), C("descending", "Descending", "sort"), C("clearFilter", "Remove Filter", "filter"), C("filter", "Filter", "filter", true)]),
            new("Records", [C("refresh", "Refresh All", "refresh", true), C("newRecord", "New", "new"), C("save", "Save", "save", shortcut: "Ctrl+S"), C("deleteRecord", "Delete", "delete"), C("totals", "Totals", "totals")]),
            new("Find", [C("find", "Find", "search", true, "Ctrl+F"), C("selectAll", "Select All", "table")]),
            new("History", [C("undo", "Undo", "undo", shortcut: "Ctrl+Z"), C("redo", "Redo", "redo", shortcut: "Ctrl+Y")])]),
        new("create", "Create", [
            new("Tables", [C("newTable", "Table", "table", true), C("tableDesign", "Table Design", "design", true)]),
            new("Queries", [C("newQuery", "Query Design", "query", true)]),
            new("Forms", [C("newForm", "Form", "form", true)]),
            new("Reports", [C("newReport", "Report", "report", true)]),
            new("Macros", [C("newMacro", "Macro", "macro", true)])]),
        new("external", "External Data", [
            new("Import & Link", [C("open", "Open Database", "open", true), C("importCsv", "Text File", "table", true)]),
            new("Export", [C("exportCsv", "Text File", "table", true), C("exportDatabase", "DataSpace File", "save", true), C("exportPdf", "PDF Report", "report", true)])]),
        new("tools", "Database Tools", [
            new("Relationships", [C("relationships", "Relationships", "relationships", true)]),
            new("Database", [C("validate", "Validate Database", "check", true), C("save", "Save Database", "save", true)]),
            new("Developer", [C("newQuery", "SQL Query", "query", true)])]),
        new("help", "Help", [new("DataSpace", [C("about", "About DataSpace", "info", true), C("shortcuts", "Keyboard Shortcuts", "info", true)])])
    ];
}
