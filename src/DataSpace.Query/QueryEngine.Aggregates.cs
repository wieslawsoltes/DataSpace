using DataSpace.Core;

namespace DataSpace.Query;

public sealed partial class QueryEngine
{
    private IEnumerable<EvaluationContext> StreamGroups(IEnumerable<EvaluationContext> rows, SelectStatement plan,
        IReadOnlyList<Projection> projections, IReadOnlyDictionary<string, object?> parameters, CancellationToken token, QueryStatistics statistics, EvaluationContext? environment = null)
    {
        var expressions = projections.Select(p => p.Expression).Concat(plan.Order.Select(o => o.Expression));
        if (plan.Having is not null) expressions = expressions.Append(plan.Having);
        var functions = expressions.SelectMany(ExpressionAnalysis.Aggregates).Distinct().ToArray();
        foreach (var function in functions) AggregateState.Validate(function);
        var groups = new Dictionary<string, AggregateGroup>(StringComparer.Ordinal);
        if (plan.Groups.Count == 0) groups.Add("", new(environment?.Clone() ?? new EvaluationContext { Parameters = parameters }, functions));
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested(); statistics.AggregateInputRows++;
            var key = plan.Groups.Count == 0 ? "" : SqlValue.Key(plan.Groups.Select(g => g.Eval(row)));
            if (!groups.TryGetValue(key, out var group))
            {
                CheckSize(groups.Count + 1); groups.Add(key, group = new(row, functions));
            }
            group.Add(row);
        }
        statistics.PeakAggregateGroups = Math.Max(statistics.PeakAggregateGroups, groups.Count);
        foreach (var group in groups.Values) { token.ThrowIfCancellationRequested(); yield return group.Complete(); }
    }
}
