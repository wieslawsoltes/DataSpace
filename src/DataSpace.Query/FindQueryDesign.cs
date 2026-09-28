using DataSpace.Core;

namespace DataSpace.Query;

public enum FindQueryKind { Duplicates, Unmatched }

/// <summary>Standalone schema-validated authoring for duplicate and unmatched queries.</summary>
public sealed class FindQueryDesign
{
    public FindQueryKind Kind { get; set; }
    public string Source { get; set; } = "";
    public string RelatedSource { get; set; } = "";
    public List<string> MatchFields { get; set; } = [];
    public List<string> RelatedFields { get; set; } = [];
    /// <summary>Empty means all source columns in detail views.</summary>
    public List<string> OutputFields { get; set; } = [];
    public bool SummaryOnly { get; set; } = true;
    public bool IncludeNullKeys { get; set; } = true;

    public string ToSql(DatabaseDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!Enum.IsDefined(Kind)) throw new DataSpaceException("Unknown query wizard kind.");
        var source = document.Table(Source);
        var keys = Fields(source, MatchFields, 1, 16);
        var output = OutputFields.Count == 0 ? source.Fields : Fields(source, OutputFields, 1, 256);
        static string Q(string alias, FieldDefinition field) => Names.Quote(alias) + "." + Names.Quote(field.Name);
        var from = Names.Quote(source.Name) + " AS [s]";
        var selected = string.Join(", ", output.Select(f => Q("s", f)));
        string sql;
        if (Kind == FindQueryKind.Duplicates)
        {
            var notNull = IncludeNullKeys ? "" : " WHERE " + string.Join(" AND ", keys.Select(f => Q("s", f) + " IS NOT NULL"));
            if (SummaryOnly)
            {
                var grouping = string.Join(", ", keys.Select(f => Q("s", f)));
                var countName = Names.Available("DuplicateCount", keys.Select(f => f.Name));
                sql = "SELECT " + grouping + ", COUNT(*) AS " + Names.Quote(countName) + " FROM " + from + notNull +
                    " GROUP BY " + grouping + " HAVING COUNT(*) > 1 ORDER BY " + grouping + ";";
            }
            else if (keys.Count == 1)
            {
                // Group the independent key set once instead of running a
                // correlated COUNT for every source row. Use the executor's
                // existing bounded per-execution cache and membership index.
                var left = Q("s", keys[0]);
                var right = Q("d", keys[0]);
                var membership = left + " IN (SELECT " + right + " FROM " + Names.Quote(source.Name) +
                    " AS [d] WHERE " + right + " IS NOT NULL GROUP BY " + right + " HAVING COUNT(*) > 1)";
                // SQL NULL is not equal to NULL. A separate independent count
                // preserves the wizard's explicit IncludeNullKeys option.
                // Test the NULL branch first so duplicate nulls do not probe IN.
                var predicate = IncludeNullKeys
                    ? "(" + left + " IS NULL AND (SELECT COUNT(*) FROM " + Names.Quote(source.Name) +
                        " AS [d] WHERE " + right + " IS NULL) > 1) OR " + membership
                    : membership;
                sql = "SELECT " + selected + " FROM " + from + " WHERE " + predicate + ";";
            }
            else
            {
                // Composite keys retain the general, bounded correlated path.
                // No primary key is required: count all matching rows, including
                // the current row. Null equality is explicit, never ordinary '='.
                var matches = string.Join(" AND ", keys.Select(f => IncludeNullKeys
                    ? "(" + Q("d", f) + " = " + Q("s", f) + " OR (" + Q("d", f) + " IS NULL AND " + Q("s", f) + " IS NULL))"
                    : Q("d", f) + " = " + Q("s", f)));
                sql = "SELECT " + selected + " FROM " + from + notNull + (notNull.Length == 0 ? " WHERE " : " AND ") +
                    "(SELECT COUNT(*) FROM " + Names.Quote(source.Name) + " AS [d] WHERE " + matches + ") > 1;";
            }
        }
        else
        {
            var related = document.Table(RelatedSource);
            var relatedKeys = Fields(related, RelatedFields, keys.Count, keys.Count);
            for (var i = 0; i < keys.Count; i++)
                if (Family(keys[i].Type) != Family(relatedKeys[i].Type)) throw new DataSpaceException("Match fields must have compatible data types.");
            if (keys.Count == 1)
            {
                // Remove inner nulls and include outer nulls explicitly. This is
                // equivalent to NOT EXISTS equality, and the independent IN set
                // can be cached/indexed once instead of rescanning for every row.
                var left = Q("s", keys[0]); var right = Q("r", relatedKeys[0]);
                sql = "SELECT " + selected + " FROM " + from + " WHERE " + left + " IS NULL OR " + left +
                    " NOT IN (SELECT " + right + " FROM " + Names.Quote(related.Name) + " AS [r] WHERE " + right + " IS NOT NULL);";
            }
            else
            {
                var matches = string.Join(" AND ", keys.Select((f, i) => Q("s", f) + " = " + Q("r", relatedKeys[i])));
                sql = "SELECT " + selected + " FROM " + from + " LEFT JOIN " + Names.Quote(related.Name) +
                    " AS [r] ON " + matches + " WHERE " + Q("r", relatedKeys[0]) + " IS NULL;";
            }
        }
        _ = new SqlParser(sql).Parse(); return sql;
    }
    private static List<FieldDefinition> Fields(TableDefinition table, List<string> names, int min, int max)
    {
        if (names.Count < min || names.Count > max) throw new DataSpaceException($"Select {min}–{max} fields for '{table.Name}'.");
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) throw new DataSpaceException("A field cannot be selected twice.");
        return names.Select(table.Field).ToList();
    }
    private static int Family(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.AutoNumber or FieldType.Decimal or FieldType.Currency or FieldType.YesNo => 0,
        FieldType.ShortText or FieldType.LongText => 1, FieldType.DateTime => 2, FieldType.Guid => 3,
        _ => throw new DataSpaceException("Unsupported match-field type.")
    };
}
