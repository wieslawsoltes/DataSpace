using System.Text;
using System.Text.Json;
using DataSpace.Core;

namespace DataSpace.Query;

public enum QueryJoinKind { Inner, Left, Cross }
public enum QueryTotal { None, GroupBy, Sum, Avg, Min, Max, Count, First, Last, Where }
public enum QuerySort { None, Ascending, Descending }

/// <summary>A detached SELECT design. SQL remains authoritative; no application or renderer dependency.</summary>
public sealed class QueryDesign
{
    public List<QueryDesignSource> Sources { get; set; } = [];
    public List<QueryDesignColumn> Columns { get; set; } = [];
    public bool Distinct { get; set; }
    public int? Top { get; set; }
    public int Offset { get; set; }
    public string Where { get; set; } = "";
    public string Having { get; set; } = "";

    public QueryDesignSource AddTable(DatabaseDocument document, string tableName)
    {
        var table = document.Table(tableName);
        var alias = table.Name;
        if (Sources.Any(s => Names.Equal(s.Alias, alias))) alias = Names.Available(table.Name + "_", Sources.Select(s => s.Alias));
        var source = new QueryDesignSource { Table = table.Name, Alias = alias, X = 24 + Sources.Count % 4 * 250, Y = 20 + Sources.Count / 4 * 260 };
        // An existing relationship can suggest a join, but never silently invent one.
        foreach (var previous in Sources)
        {
            var relation = document.Relationships.FirstOrDefault(r => Names.Equal(r.ParentTable, previous.Table) && Names.Equal(r.ChildTable, table.Name));
            var reverse = relation is null;
            relation ??= document.Relationships.FirstOrDefault(r => Names.Equal(r.ChildTable, previous.Table) && Names.Equal(r.ParentTable, table.Name));
            if (relation is null) continue;
            source.Join = QueryJoinKind.Inner;
            source.Condition = Field(previous.Alias, reverse ? relation.ChildField : relation.ParentField) + " = " + Field(alias, reverse ? relation.ParentField : relation.ChildField);
            break;
        }
        Sources.Add(source); return source;
    }
    public QueryDesignColumn AddField(string alias, string field)
    {
        if (!Sources.Any(s => Names.Equal(s.Alias, alias))) throw new DataSpaceException("Unknown query source: " + alias);
        var column = new QueryDesignColumn { Expression = field == "*" ? Names.Quote(alias) + ".*" : Field(alias, field) };
        Columns.Add(column); return column;
    }
    public static string Field(string alias, string field) => Names.Quote(alias) + "." + Names.Quote(field);
    public string ToSql() => QueryDesignSql.Generate(this);
    public static QueryDesign FromSql(string sql) => QueryDesignSql.Parse(sql);
    public string Serialize() { _ = ToSql(); return JsonSerializer.Serialize(this); }
    public static QueryDesign Restore(string sql, string? state)
    {
        // Stale or malformed view state must never replace SQL supplied by a user.
        if (!string.IsNullOrEmpty(state) && state.Length <= 524288)
        {
            try
            {
                var design = JsonSerializer.Deserialize<QueryDesign>(state);
                if (design is not null && design.ToSql() == sql) return design;
            }
            catch (Exception error) when (error is JsonException or DataSpaceException or ArgumentException or NullReferenceException) { }
        }
        return FromSql(sql);
    }
}
public sealed class QueryDesignSource
{
    public string Table { get; set; } = "";
    public string Alias { get; set; } = "";
    public QueryJoinKind Join { get; set; } = QueryJoinKind.Cross;
    public string Condition { get; set; } = "";
    public double X { get; set; } = 24;
    public double Y { get; set; } = 20;
}
public sealed class QueryDesignColumn
{
    public string Expression { get; set; } = "";
    public string Alias { get; set; } = "";
    public bool Show { get; set; } = true;
    public QueryTotal Total { get; set; }
    public QuerySort Sort { get; set; }
    public int SortPriority { get; set; }
    public List<string> Criteria { get; set; } = ["", ""];
}

/// <summary>Lossless semantic conversion for the engine's SELECT dialect. Action SQL is rejected, never replaced.</summary>
public static class QueryDesignSql
{
    public static QueryDesign Parse(string sql)
    {
        if (new SqlParser(sql).Parse() is not SelectStatement plan) throw new DataSpaceException("Design View supports SELECT queries. This statement remains available in SQL View.");
        var design = new QueryDesign { Distinct = plan.Distinct, Top = plan.Limit, Offset = plan.Offset,
            Where = plan.Where is null ? "" : SqlText.Format(plan.Where), Having = plan.Having is null ? "" : SqlText.Format(plan.Having) };
        if (plan.Source is { } source) design.Sources.Add(new() { Table = source.Table, Alias = source.Alias });
        foreach (var join in plan.Joins) design.Sources.Add(new() { Table = join.Source.Table, Alias = join.Source.Alias,
            Join = join.Kind == "LEFT" ? QueryJoinKind.Left : join.Kind == "INNER" ? QueryJoinKind.Inner : QueryJoinKind.Cross,
            Condition = join.Condition is null ? "" : SqlText.Format(join.Condition), X = 24 + design.Sources.Count % 4 * 250, Y = 20 + design.Sources.Count / 4 * 260 });
        foreach (var projection in plan.Projections)
        {
            var column = new QueryDesignColumn { Alias = projection.Alias ?? "" };
            column.Expression = projection.Wildcard is { } wildcard ? (wildcard.Length == 0 ? "*" : Names.Quote(wildcard) + ".*") : SqlText.Format(projection.Expression);
            if (projection.Wildcard is null && projection.Expression is FunctionExpr function && function.Arguments.Count == 1 &&
                Enum.TryParse<QueryTotal>(function.Name, true, out var total) && total is >= QueryTotal.Sum and <= QueryTotal.Last)
            { column.Total = total; column.Expression = SqlText.Format(function.Arguments[0]); }
            else if (plan.Groups.Any(g => SqlText.Same(g, projection.Expression))) column.Total = QueryTotal.GroupBy;
            design.Columns.Add(column);
        }
        foreach (var group in plan.Groups)
            if (!design.Columns.Any(c => c.Total == QueryTotal.GroupBy && SqlText.Same(SqlText.Parse(c.Expression), group)))
                design.Columns.Add(new() { Expression = SqlText.Format(group), Show = false, Total = QueryTotal.GroupBy });
        for (var index = 0; index < plan.Order.Count; index++)
        {
            var order = plan.Order[index];
            // Keep the actual ORDER expression, including aliases and ordinals, rather than resolving it incorrectly.
            design.Columns.Add(new() { Expression = SqlText.Format(order.Expression), Show = false, Sort = order.Descending ? QuerySort.Descending : QuerySort.Ascending, SortPriority = index + 1 });
        }
        return design;
    }
    public static string Generate(QueryDesign design)
    {
        if (design.Sources is null || design.Columns is null || design.Columns.Count is < 1 or > 256 || design.Sources.Count > 32)
            throw new DataSpaceException("A design requires 1–256 columns and at most 32 sources.");
        if (design.Top < 0 || design.Offset < 0) throw new DataSpaceException("Top and Offset must be non-negative.");
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in design.Sources)
        {
            Names.Validate(source.Table); Names.Validate(source.Alias);
            if (!aliases.Add(source.Alias)) throw new DataSpaceException("Query source aliases must be unique.");
            if (!Enum.IsDefined(source.Join) || !double.IsFinite(source.X) || !double.IsFinite(source.Y) || source.X is < 0 or > 10000 || source.Y is < 0 or > 10000)
                throw new DataSpaceException("Invalid query source position or join type.");
        }
        var expressions = design.Columns.Select(Expression).ToArray();
        var grouped = (!string.IsNullOrWhiteSpace(design.Having) && SqlText.Parse(design.Having).Aggregate) || design.Columns.Any(c => c.Total is >= QueryTotal.GroupBy and <= QueryTotal.Last) || expressions.Any(e => e != "*" && !e.EndsWith(".*", StringComparison.Ordinal) && SqlText.Parse(e).Aggregate);
        var groups = design.Columns.Where(c => c.Total == QueryTotal.GroupBy).Select(c => SqlText.Parse(c.Expression)).ToList();
        var visible = design.Columns.Select((c, i) => (c, i)).Where(p => p.c.Show && p.c.Total != QueryTotal.Where).ToArray();
        if (visible.Length == 0) throw new DataSpaceException("Select Show for at least one output column.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, i) in visible)
        {
            if (column.Alias.Length > 0 && (!names.Add(column.Alias))) throw new DataSpaceException("Output aliases must be unique.");
            if (column.Alias.Length > 0) Names.Validate(column.Alias);
            if (grouped && (expressions[i] == "*" || expressions[i].EndsWith(".*", StringComparison.Ordinal) || !SqlText.Parse(expressions[i]).GroupSafe(groups)))
                throw new DataSpaceException("Every displayed column in a totals query must be grouped or aggregated.");
        }
        var builder = new StringBuilder("SELECT ");
        if (design.Distinct) builder.Append("DISTINCT ");
        if (design.Top is { } top) builder.Append("TOP ").Append(top).Append(' ');
        builder.AppendJoin(", ", visible.Select(p => expressions[p.i] + (p.c.Alias.Length == 0 ? "" : " AS " + Names.Quote(p.c.Alias))));
        for (var index = 0; index < design.Sources.Count; index++)
        {
            var source = design.Sources[index];
            builder.Append(index == 0 ? "\nFROM " : source.Join switch { QueryJoinKind.Inner => "\nINNER JOIN ", QueryJoinKind.Left => "\nLEFT JOIN ", _ => "\nCROSS JOIN " });
            builder.Append(Names.Quote(source.Table));
            if (!Names.Equal(source.Table, source.Alias)) builder.Append(" AS ").Append(Names.Quote(source.Alias));
            if (index > 0 && source.Join != QueryJoinKind.Cross)
            {
                var condition = SqlText.Parse(source.Condition);
                if (condition.Aggregate) throw new DataSpaceException("Join conditions cannot contain aggregates.");
                builder.Append(" ON ").Append(SqlText.Format(condition));
            }
        }
        var pre = new List<string>(); var post = new List<string>();
        var rows = design.Columns.Max(c => c.Criteria.Count);
        var phases = new HashSet<bool>(); var activeRows = 0;
        for (var row = 0; row < rows; row++)
        {
            var before = new List<string>(); var after = new List<string>();
            for (var i = 0; i < design.Columns.Count; i++)
            {
                var column = design.Columns[i]; var criterion = column.Criteria.ElementAtOrDefault(row);
                if (string.IsNullOrWhiteSpace(criterion)) continue;
                var afterGroup = grouped && column.Total != QueryTotal.Where;
                phases.Add(afterGroup);
                (afterGroup ? after : before).Add(QueryCriteria.Compile(expressions[i], criterion));
            }
            if (before.Count + after.Count > 0) activeRows++;
            if (before.Count > 0) pre.Add("(" + string.Join(" AND ", before) + ")");
            if (after.Count > 0) post.Add("(" + string.Join(" AND ", after) + ")");
        }
        if (phases.Count > 1 && activeRows > 1)
            throw new DataSpaceException("OR rows cannot mix pre-group Where columns with aggregate criteria. Use the separate WHERE and HAVING expressions to make the grouping semantics explicit.");
        Clause(builder, "WHERE", design.Where, pre);
        if (groups.Count > 0) builder.Append("\nGROUP BY ").AppendJoin(", ", groups.Select(SqlText.Format));
        Clause(builder, "HAVING", design.Having, post);
        var orderings = design.Columns.Select((c, i) => (c, i)).Where(p => p.c.Sort != QuerySort.None)
            .OrderBy(p => p.c.SortPriority > 0 ? p.c.SortPriority : 1000 + p.i).ToArray();
        if (orderings.Length > 0) builder.Append("\nORDER BY ").AppendJoin(", ", orderings.Select(p => expressions[p.i] + (p.c.Sort == QuerySort.Descending ? " DESC" : " ASC")));
        if (design.Offset > 0) builder.Append("\nOFFSET ").Append(design.Offset);
        builder.Append(';');
        var sql = builder.ToString(); _ = new SqlParser(sql).Parse(); return sql;
    }
    private static string Expression(QueryDesignColumn column)
    {
        if (!Enum.IsDefined(column.Total) || !Enum.IsDefined(column.Sort) || column.Criteria is null || column.Criteria.Count > 32 || column.SortPriority < 0)
            throw new DataSpaceException("Invalid column options; at most 32 criteria rows are supported.");
        var expression = column.Expression.Trim();
        if (column.Total is >= QueryTotal.Sum and <= QueryTotal.Last) return SqlText.Format(SqlText.Parse(column.Total.ToString().ToUpperInvariant() + "(" + expression + ")"));
        if (expression == "*" || expression.EndsWith(".*", StringComparison.Ordinal))
        {
            if (column.Total != QueryTotal.None || column.Sort != QuerySort.None || column.Criteria.Any(s => !string.IsNullOrWhiteSpace(s)) || column.Alias.Length != 0)
                throw new DataSpaceException("Wildcard columns cannot have totals, sorting, criteria or aliases. Add individual fields instead.");
            // Parse as an actual projection, not by accepting arbitrary text ending in .*.
            if (new SqlParser("SELECT " + expression + ";").Parse() is not SelectStatement { Projections.Count: 1 } parsed || parsed.Projections[0].Wildcard is null)
                throw new DataSpaceException("Invalid wildcard.");
            return expression;
        }
        return SqlText.Format(SqlText.Parse(expression));
    }
    private static void Clause(StringBuilder builder, string name, string expression, List<string> rows)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(expression)) parts.Add(SqlText.Format(SqlText.Parse(expression)));
        if (rows.Count > 0) parts.Add("(" + string.Join(" OR ", rows) + ")");
        if (parts.Count > 0) builder.Append('\n').Append(name).Append(' ').AppendJoin(" AND ", parts.Select(p => "(" + p + ")"));
    }
}
