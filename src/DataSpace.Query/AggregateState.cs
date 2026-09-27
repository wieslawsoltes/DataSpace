using DataSpace.Core;

namespace DataSpace.Query;

/// <summary>Constant-space accumulator shared by GROUP BY and TRANSFORM.</summary>
internal sealed class AggregateState(FunctionExpr function)
{
    private long _count;
    private decimal _sum;
    private object? _value;
    public static void Validate(FunctionExpr function)
    {
        if (!function.IsAggregate || function.Arguments.Count != 1 || function.Arguments[0].Aggregate)
            throw new DataSpaceException("An aggregate requires exactly one non-aggregate argument.");
        if (function.Arguments[0] is StarExpr && function.Name != "COUNT")
            throw new DataSpaceException("Only COUNT accepts a wildcard argument.");
    }
    public void Add(EvaluationContext row)
    {
        var value = function.Arguments[0].Eval(row);
        if (value is null) return;
        switch (function.Name)
        {
            case "SUM": case "AVG": _sum = checked(_sum + SqlValue.Number(value)); break;
            case "MIN": if (_count == 0 || SqlValue.Compare(value, _value) < 0) _value = value; break;
            case "MAX": if (_count == 0 || SqlValue.Compare(value, _value) > 0) _value = value; break;
            case "FIRST": if (_count == 0) _value = value; break;
            case "LAST": _value = value; break;
        }
        _count++;
    }
    public object? Value => function.Name == "COUNT" ? _count : _count == 0 ? null : function.Name switch
    { "SUM" => _sum, "AVG" => _sum / _count, _ => _value };
}

internal sealed class AggregateGroup
{
    private readonly Dictionary<FunctionExpr, AggregateState> _states;
    public EvaluationContext Context { get; }
    public AggregateGroup(EvaluationContext first, IReadOnlyList<FunctionExpr> functions)
    {
        Context = first.Clone();
        _states = functions.ToDictionary(f => f, f => new AggregateState(f));
    }
    public void Add(EvaluationContext row) { foreach (var state in _states.Values) state.Add(row); }
    public EvaluationContext Complete()
    {
        Context.Aggregates = _states.ToDictionary(pair => pair.Key, pair => pair.Value.Value);
        return Context;
    }
}
