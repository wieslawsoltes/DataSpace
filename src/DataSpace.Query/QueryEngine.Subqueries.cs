using DataSpace.Core;

namespace DataSpace.Query;

public sealed partial class QueryEngine
{
    // Every public execution owns its budgets and caches. Parsed ASTs contain no
    // mutable bindings/results; concurrent calls cannot share data or parameters.
    private sealed class QueryExecution(QueryOptions options, QueryStatistics statistics, CancellationToken token)
    {
        public int Depth { get; private set; }
        public long CachedBytes { get; private set; }
        public void ReadRow()
        {
            token.ThrowIfCancellationRequested();
            if (statistics.SourceRowsRead > options.MaximumTotalSourceRows)
                throw new DataSpaceException("Total query source-row work limit exceeded.");
        }
        public void Enter()
        {
            token.ThrowIfCancellationRequested();
            if (Depth >= options.MaximumSubqueryDepth) throw new DataSpaceException("Subquery nesting limit exceeded.");
            if (statistics.SubqueryExecutions >= options.MaximumSubqueryExecutions)
                throw new DataSpaceException("Subquery execution limit exceeded. Narrow the outer query.");
            Depth++; statistics.SubqueryExecutions++;
            statistics.PeakSubqueryDepth = Math.Max(statistics.PeakSubqueryDepth, Depth);
        }
        public void Exit() => Depth--;
        public bool Reserve(long bytes)
        {
            if (bytes > options.MaximumSubqueryCacheBytes - CachedBytes) return false;
            CachedBytes += bytes; statistics.SubqueryCacheBytes = CachedBytes; return true;
        }
    }
    private sealed record OuterReference(EvaluationContext Owner, NameExpr Name);
    private sealed record SubqueryDescription(int Columns, List<OuterReference> References, bool Volatile);
    private sealed record PreparedSelect(List<(Source Source, TableDefinition Table)> Sources, List<Projection> Projections,
        List<string> Names, EvaluationContext Environment, SubqueryScope Scope, bool Grouped);

    private PreparedSelect PrepareSelect(DatabaseDocument document, SelectStatement plan,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken token, HashSet<string> path,
        QueryStatistics statistics, QueryExecution execution, EvaluationContext? outer, int depth)
    {
        token.ThrowIfCancellationRequested();
        if (depth > Options.MaximumSubqueryDepth) throw new DataSpaceException("Subquery nesting limit exceeded.");
        var sources = new List<(Source Source, TableDefinition Table)>();
        if (plan.Source is { } from) sources.Add((from, ResolveSource(document, from.Table, parameters, token, path, statistics, execution)));
        foreach (var join in plan.Joins) sources.Add((join.Source, ResolveSource(document, join.Source.Table, parameters, token, path, statistics, execution)));
        if (sources.Select(s => s.Source.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            throw new DataSpaceException("Duplicate table alias.");
        var projections = Expand(plan.Projections, sources);
        var scope = new SubqueryScope(this, document, parameters, token, path, statistics, execution, depth);
        // Saved sources can contain time-dependent functions; conservatively avoid
        // memoizing a subquery which depends on one until volatility propagates
        // through saved-source metadata without executing the source.
        if (sources.Any(s => !document.Tables.Any(t => Names.Equal(t.Name, s.Source.Table)))) scope.MarkVolatile();
        var environment = new EvaluationContext { Parameters = parameters, Outer = outer, Subqueries = scope.Evaluate };
        var schema = environment.Clone();
        foreach (var source in sources) schema = AddSource(schema, source.Table, source.Source.Alias, null);
        foreach (var projection in projections) scope.Bind(projection.Expression, schema);
        if (plan.Where is { } predicate)
        {
            if (predicate.Aggregate) throw new DataSpaceException("Aggregates belong in HAVING, not WHERE.");
            scope.Bind(predicate, schema);
        }
        foreach (var group in plan.Groups)
        {
            if (group.Aggregate) throw new DataSpaceException("GROUP BY cannot contain aggregates.");
            scope.Bind(group, schema);
        }
        foreach (var join in plan.Joins) if (join.Condition is { } on)
        {
            if (on.Aggregate) throw new DataSpaceException("Join conditions cannot contain aggregates.");
            scope.Bind(on, schema);
        }
        var names = OutputNames(projections);
        var aliases = schema.Clone(); foreach (var name in names) { aliases.Values[name] = null; aliases.Ambiguous.Remove(name); }
        if (plan.Having is { } having) scope.Bind(having, aliases);
        foreach (var order in plan.Order) scope.Bind(order.Expression, aliases);
        var grouped = plan.Groups.Count > 0 || projections.Any(p => p.Expression.Aggregate) || plan.Having?.Aggregate == true;
        if (grouped && projections.Any(p => !scope.GroupSafe(p.Expression, plan.Groups)))
            throw new DataSpaceException("Every selected field, including correlated fields, must be grouped or aggregated.");
        if (!grouped && plan.Having is not null) throw new DataSpaceException("HAVING requires grouping or aggregates.");
        return new(sources, projections, names, environment, scope, grouped);
    }

    private SubqueryDescription DescribeSubquery(Statement query, EvaluationContext outer,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken token, HashSet<string> path,
        QueryStatistics statistics, QueryExecution execution, int depth, DatabaseDocument document)
    {
        if (query is SelectStatement select)
        {
            var prepared = PrepareSelect(document, select, parameters, token, path, statistics, execution, outer, depth);
            return new(prepared.Names.Count, prepared.Scope.ExternalReferences(), prepared.Scope.Volatile);
        }
        if (query is not UnionStatement union) throw new DataSpaceException("A subquery must contain SELECT, not an action statement.");
        var descriptions = union.Queries.Select(q => DescribeSubquery(q, outer, parameters, token, path, statistics, execution, depth, document)).ToList();
        if (descriptions.Any(d => d.Columns != descriptions[0].Columns)) throw new DataSpaceException("UNION branches must have the same number of columns.");
        // Union ordering is bound again by its executor. Inner SELECT bindings and
        // all correlations are checked here even when the outer source is empty.
        return new(descriptions[0].Columns, descriptions.SelectMany(d => d.References).ToList(), descriptions.Any(d => d.Volatile) || union.Order.Any(o => ExpressionAnalysis.IsVolatile(o.Expression)));
    }

    private sealed class SubqueryScope(QueryEngine engine, DatabaseDocument document,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken token, HashSet<string> path,
        QueryStatistics statistics, QueryExecution execution, int depth)
    {
        private readonly Dictionary<SubqueryExpr, SubqueryDescription> _bindings = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SubqueryExpr, SubqueryValues> _cache = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<EvaluationContext> _localSchemas = [];
        private readonly List<OuterReference> _references = [];
        public bool Volatile { get; private set; }
        public void MarkVolatile() => Volatile = true;
        private readonly HashSet<NameExpr> _constants = new(ReferenceEqualityComparer.Instance);
        public IEnumerable<Expr> ScanReferences => _references.Select(r => (Expr)r.Name);
        public List<OuterReference> ExternalReferences() => _references.Where(r => !_localSchemas.Contains(r.Owner)).ToList();
        public void Bind(Expr expression, EvaluationContext schema)
        {
            _localSchemas.Add(schema); Volatile |= ExpressionAnalysis.IsVolatile(expression);
            foreach (var name in ExpressionAnalysis.Names(expression))
            {
                schema.Resolve(name.Name, name.Parameter);
                if (!name.Parameter && schema.FindOwner(name.Name) is { } owner) _references.Add(new(owner, name));
                else _constants.Add(name);
            }
            foreach (var subquery in ExpressionAnalysis.Subqueries(expression))
            {
                if (!_bindings.TryGetValue(subquery, out var description))
                {
                    description = engine.DescribeSubquery(subquery.Query, schema, parameters, token, path, statistics, execution, depth + 1, document);
                    if (subquery.Kind != SubqueryKind.Exists && description.Columns != 1)
                        throw new DataSpaceException("A scalar, IN, ANY or ALL subquery must return exactly one column.");
                    _bindings.Add(subquery, description);
                }
                _references.AddRange(description.References); Volatile |= description.Volatile;
            }
        }
        public bool GroupSafe(Expr expression, IReadOnlyList<Expr> groups)
        {
            if (groups.Any(g => SqlText.Same(g, expression))) return true;
            return expression switch
            {
                SubqueryExpr s => (s.Operand is null || GroupSafe(s.Operand, groups)) && _bindings[s].References.All(r => !_localSchemas.Contains(r.Owner) || r.Name.GroupSafe(groups)),
                NameExpr n => _constants.Contains(n) || _references.Any(r => ReferenceEquals(r.Name, n) && !_localSchemas.Contains(r.Owner)) || n.GroupSafe(groups),
                FunctionExpr f => f.IsAggregate || f.Arguments.All(a => GroupSafe(a, groups)),
                UnaryExpr u => GroupSafe(u.Operand, groups), BinaryExpr b => GroupSafe(b.Left, groups) && GroupSafe(b.Right, groups),
                NullExpr n => GroupSafe(n.Operand, groups), InExpr i => GroupSafe(i.Operand, groups) && i.Items.All(a => GroupSafe(a, groups)),
                _ => expression.GroupSafe(groups)
            };
        }
        public object? Evaluate(SubqueryExpr expression, EvaluationContext row)
        {
            token.ThrowIfCancellationRequested();
            if (!_bindings.TryGetValue(expression, out var binding)) throw new DataSpaceException("Subquery is not bound to this query scope.");
            if (!_cache.TryGetValue(expression, out var values))
            {
                execution.Enter();
                try
                {
                    var plan = expression.Query;
                    if (plan is SelectStatement select)
                    {
                        // EXISTS needs only cardinality. Do not evaluate projected
                        // expressions in a simple existence test. Preserve DISTINCT
                        // with OFFSET and all aggregate/HAVING cardinality rules.
                        if (expression.Kind == SubqueryKind.Exists && select.Groups.Count == 0 && select.Having is null &&
                            !select.Projections.Any(p => p.Expression.Aggregate) && select.Offset == 0)
                            plan = select with { Projections = [new(new LiteralExpr(1m))], Order = [], Distinct = false, Limit = select.Limit == 0 ? 0 : 1 };
                        else if (expression.Kind == SubqueryKind.Scalar)
                            plan = select with { Limit = Math.Min(select.Limit ?? 2, 2) };
                    }
                    var result = engine.Read(document, plan, parameters, token, path, statistics, execution, row);
                    if (expression.Kind == SubqueryKind.Scalar && result.Records.Count > 1)
                        throw new DataSpaceException("A scalar subquery returned more than one row.");
                    values = new SubqueryValues(expression.Kind == SubqueryKind.Exists ? [result.Records.Count > 0] :
                        result.Records.Select(r => FieldValues.Parse(result.Fields[0], r[result.Fields[0].Name])).ToArray());
                }
                finally { execution.Exit(); }
                if (engine.Options.EnableSubqueryCache && binding.References.Count == 0 && !binding.Volatile &&
                    _cache.Count < 128 && execution.Reserve(values.EstimatedBytes)) _cache.Add(expression, values);
            }
            else statistics.SubqueryCacheHits++;
            if (expression.Kind == SubqueryKind.Exists) return values.Values[0];
            if (expression.Kind == SubqueryKind.Scalar) return values.Values.FirstOrDefault();
            var operand = expression.Operand!.Eval(row);
            if (expression.Kind == SubqueryKind.In && engine.Options.EnableMembershipIndexes && values.TryContains(operand, out var found))
            {
                statistics.MembershipIndexProbes++;
                return found ? !expression.Negated : values.HasNull ? null : expression.Negated;
            }
            var unknown = false;
            foreach (var value in values.Values)
            {
                token.ThrowIfCancellationRequested(); statistics.SubqueryComparisons++;
                if (operand is null || value is null) { unknown = true; continue; }
                var compare = SqlValue.Compare(operand, value);
                var match = expression.Comparison switch
                {
                    "=" => compare == 0, "<>" or "!=" => compare != 0, "<" => compare < 0,
                    ">" => compare > 0, "<=" => compare <= 0, ">=" => compare >= 0, _ => false
                };
                if (expression.Kind == SubqueryKind.All && !match) return false;
                if (expression.Kind != SubqueryKind.All && match) return !expression.Negated;
            }
            if (unknown) return null;
            return expression.Kind == SubqueryKind.All || expression.Kind == SubqueryKind.In && expression.Negated;
        }
    }
    private sealed class SubqueryValues(object?[] values)
    {
        public object?[] Values { get; } = values;
        public bool HasNull { get; } = values.Any(v => v is null);
        // Includes a conservative allowance for a membership hash table. This is
        // a cache admission estimate, not a process/GC memory measurement.
        public long EstimatedBytes { get; } = 128L + values.Sum(v => v is string s ? 96L + s.Length * 2L : 96L);
        private HashSet<object>? _index;
        private int? _family;
        private static int Family(object value) => value switch
        { decimal or long or int or double or bool => 0, string => 1, DateTime => 2, _ => -1 };
        public bool TryContains(object? operand, out bool found)
        {
            found = false;
            if (operand is null) return false;
            if (_family is null)
            {
                var families = Values.Where(v => v is not null).Select(v => Family(v!)).Distinct().ToArray();
                _family = families.Length == 1 ? families[0] : -1;
            }
            if (_family < 0 || Family(operand) != _family) return false;
            _index ??= new(Values.Where(v => v is not null).Select(v => Canonical(v!)), new MembershipComparer(_family.Value));
            found = _index.Contains(Canonical(operand)); return true;
        }
        private static object Canonical(object value) => Family(value) == 0 ? SqlValue.Number(value) : value;
        private sealed class MembershipComparer(int family) : IEqualityComparer<object>
        {
            public new bool Equals(object? x, object? y) => SqlValue.Compare(x, y) == 0;
            public int GetHashCode(object value) => family == 1 ? StringComparer.OrdinalIgnoreCase.GetHashCode((string)value)
                : family == 2 ? ((DateTime)value).Ticks.GetHashCode() : ((decimal)value).GetHashCode();
        }
    }
}
