namespace DataSpace.Controls;

/// <summary>Contextual tabs expose implemented editor commands; unsupported Access commands are not advertised.</summary>
public static class OfficeContextCatalog
{
    private static RibbonCommand C(string id, string text, string icon = "table", bool large = false) => new(id, text, icon, large);
    public static RibbonTab? Create(DatabaseObjectKind? kind, bool design) => kind switch
    {
        DatabaseObjectKind.Table when !design => new("tableLayout", "Table Fields", [
            new("Views", [C("designView", "Design View", "design", true)]),
            new("Fields", [C("columns", "Unhide Fields"), C("hideFields", "Hide Fields"), C("columnWidth", "Column Width"), C("bestFit", "Best Fit")]),
            new("Freeze", [C("freezeFields", "Freeze Fields"), C("unfreezeFields", "Unfreeze All Fields")]),
            new("Formatting", [C("formatDatasheet", "Datasheet Formatting", "design", true), C("totals", "Totals", "totals")]),
            new("Find", [C("find", "Find", "find"), C("replace", "Replace", "replace")])], true),
        DatabaseObjectKind.Table => new("tableDesign", "Table Design", [
            new("Views", [C("datasheetView", "Datasheet View", "table", true)]),
            new("Tools", [C("fieldPrimaryKey", "Primary Key", "key", true), C("fieldAdd", "Insert Field", "new"), C("fieldDelete", "Delete Field", "delete"), C("fieldUp", "Move Up"), C("fieldDown", "Move Down"), C("fieldIndexes", "Indexes", "table", true)]),
            new("Show / Hide", [C("propertySheet", "Property Sheet", "form", true)])], true),
        DatabaseObjectKind.Query => new("queryDesign", "Query Design", [
            new("Views", [C("queryDesignView", "Design View", "design"), C("querySqlView", "SQL View", "sql"), C("queryResults", "Datasheet View")]),
            new("Results", [C("queryRun", "Run", "run", true), C("exportCsv", "Export Results", "export")]),
            new("Builders", [C("queryCrosstab", "Crosstab Builder", "query"), C("queryDuplicates", "Find Duplicates", "query"), C("queryUnmatched", "Find Unmatched", "query")])], true),
        DatabaseObjectKind.Form when design => new("formDesign", "Form Design", [
            new("Views", [C("datasheetView", "Form View", "form", true)]),
            new("Controls", [C("formLabel", "Label", "form"), C("formHeading", "Heading", "form"), C("formDelete", "Delete Control", "delete")]),
            new("Tools", [C("propertySheet", "Property Sheet", "form", true), C("save", "Save", "save")])], true),
        DatabaseObjectKind.Form => new("formView", "Form", [new("Views", [C("designView", "Design View", "design", true)]),
            new("Records", [C("newRecord", "New", "new"), C("save", "Save", "save"), C("refresh", "Refresh", "refresh")])], true),
        DatabaseObjectKind.Report => new("reportPreview", "Print Preview", [
            new("Views", [C(design ? "datasheetView" : "designView", design ? "Report View" : "Design View", "report", true)]),
            new("Pages", [C("reportPrevious", "Previous Page"), C("reportNext", "Next Page")]),
            new("Page Layout", [C("reportPortrait", "Portrait", "report"), C("reportLandscape", "Landscape", "report")]),
            new("Zoom", [C("reportZoomOut", "Zoom Out", "find"), C("reportZoomIn", "Zoom In", "find")]),
            new("Export", [C("exportPdf", "PDF Report", "export", true)])], true),
        _ => null
    };
}
