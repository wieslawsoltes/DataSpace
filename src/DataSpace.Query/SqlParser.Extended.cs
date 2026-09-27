using DataSpace.Core;

namespace DataSpace.Query;

internal sealed record TransformStatement(FunctionExpr Aggregate, SelectStatement Query, Expr Pivot, List<Expr>? Headings) : Statement;
internal sealed record MakeTableStatement(string Table, SelectStatement Query) : Statement;
internal sealed record CreateIndexStatement(string Table, IndexDefinition Index) : Statement;
internal sealed record DropIndexStatement(string Table, string Index) : Statement;

internal sealed partial class SqlParser
{
    private Statement SelectOrMakeTable()
    {
        var select = Select();
        return select.Into is { } table ? new MakeTableStatement(table, select with { Into = null }) : ReadQuery(select);
    }
    private TransformStatement Transform()
    {
        if (Expression() is not FunctionExpr aggregate || !aggregate.IsAggregate)
            throw Error("TRANSFORM requires one aggregate function");
        AggregateState.Validate(aggregate);
        Expect("SELECT"); var query = Select();
        if (query.Into is not null || query.Source is null || query.Groups.Count == 0 || query.Distinct || query.Limit is not null || query.Offset != 0)
            throw Error("TRANSFORM requires FROM and GROUP BY, without INTO, DISTINCT, TOP, LIMIT or OFFSET");
        Expect("PIVOT");
        // IN here declares headings, not an IN predicate. Parenthesized expressions
        // and function arguments can still contain comparisons.
        var pivot = Expression(4); if (pivot.Aggregate) throw Error("The pivot expression cannot contain aggregates");
        List<Expr>? headings = null;
        if (Match("IN"))
        {
            Expect("("); headings = [];
            do { headings.Add(Expression()); if (headings.Count > 256) throw Error("At most 256 fixed headings are supported"); } while (Match(","));
            Expect(")");
        }
        return new(aggregate, query, pivot, headings);
    }
    private Statement Create()
    {
        var unique = Match("UNIQUE");
        if (unique || Is("INDEX"))
        {
            Expect("INDEX"); var name = Identifier(); Expect("ON"); var table = Identifier(); Expect("(");
            var fields = new List<string>();
            do
            {
                fields.Add(Identifier()); Match("ASC");
                if (Is("DESC")) throw Error("Descending persistent index keys are not implemented; index definitions currently enforce constraints only");
            } while (Match(","));
            Expect(")"); return new CreateIndexStatement(table, new() { Name = name, Fields = fields, Unique = unique });
        }
        Expect("TABLE"); var definition = new TableDefinition { Name = Identifier() }; Expect("(");
        do definition.Fields.Add(Field()); while (Match(",")); Expect(")"); return new CreateStatement(definition);
    }
    private Statement Drop()
    {
        if (Match("INDEX")) { var name = Identifier(); Expect("ON"); return new DropIndexStatement(Identifier(), name); }
        Expect("TABLE"); return new DropStatement(Identifier());
    }
}
