using DataSpace.Core;

namespace DataSpace.Query;

public sealed partial class QueryEngine
{
    private IReadOnlyList<FieldDefinition>? PrunedFields(TableDefinition table, string alias, IEnumerable<Expr> expressions)
    {
        if (!Options.EnableColumnPruning) return null;
        // Wildcards have already been expanded and binding already checked. Explicit
        // parameters do not read a field even when they have the same name as one.
        var names = expressions.SelectMany(ExpressionAnalysis.Names).Where(name => !name.Parameter)
            .Select(name => name.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return table.Fields.Where(field => names.Contains(field.Name) || names.Contains(alias + "." + field.Name)).ToArray();
    }
    private IEnumerable<EvaluationContext> SourceRows(TableDefinition table, string alias, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken token, QueryStatistics statistics, bool reuseContext = false, IReadOnlyList<FieldDefinition>? selectedFields = null,
        EvaluationContext? environment = null, QueryExecution? execution = null)
    {
        var count = 0;
        if (!reuseContext && selectedFields is null)
        {
            // Retain the unoptimized path for differential tests and joined inputs.
            foreach (var record in table.Records)
            {
                token.ThrowIfCancellationRequested(); CheckSize(++count); statistics.SourceRowsRead++; execution?.ReadRow(); statistics.SourceContextsCreated++;
                statistics.SourceValuesRead += table.Fields.Count;
                yield return AddSource((environment?.Clone() ?? new EvaluationContext { Parameters = parameters }), table, alias, record);
            }
            yield break;
        }
        // Only direct single-source streaming consumers may reuse a context. Joins
        // and reference groups retain rows and must receive distinct contexts.
        var fields = (selectedFields ?? table.Fields).Select(field => (Field: field, Qualified: alias + "." + field.Name)).ToArray();
        EvaluationContext? shared = null;
        if (reuseContext) { shared = (environment?.Clone() ?? new EvaluationContext { Parameters = parameters }); statistics.SourceContextsCreated++; }
        foreach (var record in table.Records)
        {
            token.ThrowIfCancellationRequested(); CheckSize(++count); statistics.SourceRowsRead++; execution?.ReadRow();
            var context = shared ?? (environment?.Clone() ?? new EvaluationContext { Parameters = parameters });
            if (shared is null) statistics.SourceContextsCreated++;
            context.Aliases.Add(alias);
            foreach (var (field, qualified) in fields)
            {
                var value = FieldValues.Parse(field, record[field.Name]); context.Values[field.Name] = value; context.Values[qualified] = value;
                statistics.SourceValuesRead++;
            }
            yield return context;
        }
    }
}
