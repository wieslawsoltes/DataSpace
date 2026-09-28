namespace DataSpace.Query;

internal static class ExpressionAnalysis
{
    public static IEnumerable<NameExpr> Names(Expr expression)
    {
        if (expression is NameExpr name) { yield return name; yield break; }
        IEnumerable<Expr> children = expression switch
        {
            UnaryExpr unary => [unary.Operand], BinaryExpr binary => [binary.Left, binary.Right],
            NullExpr nullCheck => [nullCheck.Operand], InExpr list => new[] { list.Operand }.Concat(list.Items),
            FunctionExpr function => function.Arguments,
            SubqueryExpr { Operand: { } operand } => [operand], _ => []
        };
        foreach (var child in children) foreach (var item in Names(child)) yield return item;
    }
    public static IEnumerable<FunctionExpr> Aggregates(Expr expression)
    {
        if (expression is FunctionExpr { IsAggregate: true } aggregate) { yield return aggregate; yield break; }
        IEnumerable<Expr> children = expression switch
        {
            UnaryExpr unary => [unary.Operand], BinaryExpr binary => [binary.Left, binary.Right],
            NullExpr nullCheck => [nullCheck.Operand], InExpr list => new[] { list.Operand }.Concat(list.Items),
            FunctionExpr function => function.Arguments,
            SubqueryExpr { Operand: { } operand } => [operand], _ => []
        };
        foreach (var child in children) foreach (var found in Aggregates(child)) yield return found;
    }
    public static IEnumerable<SubqueryExpr> Subqueries(Expr expression)
    {
        if (expression is SubqueryExpr subquery) yield return subquery;
        IEnumerable<Expr> children = expression switch
        {
            UnaryExpr u => [u.Operand], BinaryExpr b => [b.Left, b.Right], NullExpr n => [n.Operand],
            InExpr i => new[] { i.Operand }.Concat(i.Items), FunctionExpr f => f.Arguments,
            SubqueryExpr { Operand: { } operand } => [operand], _ => []
        };
        foreach (var child in children) foreach (var query in Subqueries(child)) yield return query;
    }
    public static bool IsVolatile(Expr expression) => expression switch
    {
        FunctionExpr f => f.Name is "NOW" or "DATE" || f.Arguments.Any(IsVolatile),
        UnaryExpr u => IsVolatile(u.Operand), BinaryExpr b => IsVolatile(b.Left) || IsVolatile(b.Right),
        NullExpr n => IsVolatile(n.Operand), InExpr i => IsVolatile(i.Operand) || i.Items.Any(IsVolatile),
        SubqueryExpr { Operand: { } operand } => IsVolatile(operand), _ => false
    };
    public static IEnumerable<Expr> Conjuncts(Expr expression)
    {
        if (expression is BinaryExpr { Op: "AND" } conjunction)
        { foreach (var part in Conjuncts(conjunction.Left)) yield return part; foreach (var part in Conjuncts(conjunction.Right)) yield return part; }
        else yield return expression;
    }
}
