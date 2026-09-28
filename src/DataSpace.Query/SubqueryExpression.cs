using DataSpace.Core;

namespace DataSpace.Query;

internal enum SubqueryKind { Scalar, Exists, In, Any, All }

// Inner names and aggregates belong to their own lexical scope, never the outer
// expression's aggregate list. The operand, when present, remains in outer scope.
internal sealed record SubqueryExpr(Statement Query, string Sql, SubqueryKind Kind,
    Expr? Operand = null, string Comparison = "=", bool Negated = false) : Expr
{
    public override bool Aggregate => Operand?.Aggregate == true;
    public override object? Eval(EvaluationContext context) => context.Subqueries is { } evaluate ? evaluate(this, context)
        : throw new DataSpaceException("Subqueries require a database query context.");
    public string ToSql() => Kind switch
    {
        SubqueryKind.Scalar => "(" + Sql + "\n)",
        SubqueryKind.Exists => "EXISTS (" + Sql + "\n)",
        SubqueryKind.In => "(" + SqlText.Format(Operand!) + (Negated ? " NOT IN (" : " IN (") + Sql + "\n))",
        _ => "(" + SqlText.Format(Operand!) + " " + Comparison + (Kind == SubqueryKind.All ? " ALL (" : " ANY (") + Sql + "\n))"
    };
}
