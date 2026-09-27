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
            FunctionExpr function => function.Arguments, _ => []
        };
        foreach (var child in children) foreach (var item in Names(child)) yield return item;
    }
    public static IEnumerable<Expr> Conjuncts(Expr expression)
    {
        if (expression is BinaryExpr { Op: "AND" } conjunction)
        { foreach (var part in Conjuncts(conjunction.Left)) yield return part; foreach (var part in Conjuncts(conjunction.Right)) yield return part; }
        else yield return expression;
    }
}
