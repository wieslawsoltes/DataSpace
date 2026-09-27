using DataSpace.Core;

namespace DataSpace.Query;

public enum CrosstabAggregate { Sum, Count, Avg, Min, Max, First, Last }

/// <summary>Reusable single-table crosstab authoring model. Advanced TRANSFORM SQL remains editable in SQL View.</summary>
public sealed class CrosstabDesign
{
    public string Source { get; set; } = "";
    public List<string> RowFields { get; set; } = [];
    public string ColumnExpression { get; set; } = "";
    public string ValueExpression { get; set; } = "*";
    public CrosstabAggregate Aggregate { get; set; } = CrosstabAggregate.Count;
    public string FixedHeadings { get; set; } = "";
    public string Where { get; set; } = "";
    public bool ShowRowTotals { get; set; }

    public string ToSql(DatabaseDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var table = document.Table(Source);
        if (RowFields.Count == 0 || RowFields.Count > 32 || RowFields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != RowFields.Count)
            throw new DataSpaceException("Select 1–32 distinct row-heading fields.");
        if (!Enum.IsDefined(Aggregate)) throw new DataSpaceException("Unknown crosstab aggregate.");
        var rows = string.Join(", ", RowFields.Select(field => Names.Quote(table.Field(field).Name)));
        var pivot = ParseScalar(ColumnExpression); var value = ValueExpression.Trim() == "*" ? new StarExpr() : ParseScalar(ValueExpression);
        var function = new FunctionExpr(Aggregate.ToString().ToUpperInvariant(), [value]); AggregateState.Validate(function);
        var sql = "TRANSFORM " + SqlText.Format(function) + "\nSELECT " + rows;
        if (ShowRowTotals) sql += ", " + SqlText.Format(function) + " AS [Row Total]";
        sql += "\nFROM " + Names.Quote(table.Name);
        if (!string.IsNullOrWhiteSpace(Where)) sql += "\nWHERE " + SqlText.Format(ParseScalar(Where));
        sql += "\nGROUP BY " + rows + "\nORDER BY " + rows + "\nPIVOT " + SqlText.Format(pivot);
        if (!string.IsNullOrWhiteSpace(FixedHeadings)) sql += " IN (" + FixedHeadings + ")";
        sql += ";";
        if (new SqlParser(sql).Parse() is not TransformStatement plan) throw new DataSpaceException("Invalid crosstab definition.");
        if (plan.Headings is not null)
            foreach (var heading in plan.Headings)
                if (heading.Aggregate || ExpressionAnalysis.Names(heading).Any(name => !name.Parameter))
                    throw new DataSpaceException("Fixed headings must be literal values or explicit @parameters, not field names.");
        // Binding and data-dependent heading collisions are checked by the executor on Run.
        return sql;
    }
    private static Expr ParseScalar(string text)
    {
        var parser = new SqlParser(text); var expression = parser.Expression(); parser.End();
        if (expression.Aggregate) throw new DataSpaceException("This expression cannot contain an aggregate.");
        return expression;
    }
    public static CrosstabDesign FromSql(string sql)
    {
        if (new SqlParser(sql).Parse() is not TransformStatement plan || plan.Query.Source is not { } source ||
            !Names.Equal(source.Table, source.Alias) || plan.Query.Joins.Count != 0 || plan.Query.Having is not null ||
            plan.Query.Groups.Any(group => group is not NameExpr { Parameter: false }))
            throw new DataSpaceException("This query requires SQL View; the builder edits single-table crosstabs without HAVING or source aliases.");
        var query = plan.Query; var rows = query.Groups.Cast<NameExpr>().Select(group => group.Name).ToList();
        if (rows.Any(name => name.Contains('.')) || query.Projections.Count < rows.Count || query.Projections.Count > rows.Count + 1)
            throw new DataSpaceException("This row-heading layout requires SQL View.");
        for (var i = 0; i < rows.Count; i++)
            if (query.Projections[i].Alias is not null || query.Projections[i].Wildcard is not null || !SqlText.Same(query.Projections[i].Expression, query.Groups[i]))
                throw new DataSpaceException("This row-heading layout requires SQL View.");
        var totals = query.Projections.Count > rows.Count;
        if (totals && (!Names.Equal(query.Projections[^1].Alias, "Row Total") || !SqlText.Same(query.Projections[^1].Expression, plan.Aggregate)))
            throw new DataSpaceException("Custom row totals require SQL View.");
        if (query.Order.Count != rows.Count || query.Order.Where((order, i) => order.Descending || !SqlText.Same(order.Expression, query.Groups[i])).Any())
            throw new DataSpaceException("Custom row ordering requires SQL View.");
        return new()
        {
            Source = source.Table, RowFields = rows, Aggregate = Enum.Parse<CrosstabAggregate>(plan.Aggregate.Name, true),
            ColumnExpression = SqlText.Format(plan.Pivot), ValueExpression = SqlText.Format(plan.Aggregate.Arguments[0]),
            Where = query.Where is null ? "" : SqlText.Format(query.Where), ShowRowTotals = totals,
            FixedHeadings = plan.Headings is null ? "" : string.Join(", ", plan.Headings.Select(SqlText.Format))
        };
    }
}
