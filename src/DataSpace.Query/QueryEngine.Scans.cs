using DataSpace.Core;

namespace DataSpace.Query;

public sealed partial class QueryEngine
{
    private IEnumerable<EvaluationContext> SourceRows(TableDefinition table, string alias, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken token, QueryStatistics statistics, bool reuseContext = false)
    {
        var count = 0;
        if (!reuseContext)
        {
            foreach (var record in table.Records)
            {
                token.ThrowIfCancellationRequested(); CheckSize(++count); statistics.SourceRowsRead++; statistics.SourceContextsCreated++;
                yield return AddSource(new EvaluationContext { Parameters = parameters }, table, alias, record);
            }
            yield break;
        }
        // Only direct single-source streaming consumers may select this path. Joins
        // and buffered reference groups retain contexts and must receive distinct ones.
        var context = new EvaluationContext { Parameters = parameters }; statistics.SourceContextsCreated++;
        var fields = table.Fields.Select(field => (Field: field, Qualified: alias + "." + field.Name)).ToArray();
        foreach (var record in table.Records)
        {
            token.ThrowIfCancellationRequested(); CheckSize(++count); statistics.SourceRowsRead++;
            foreach (var (field, qualified) in fields)
            {
                var value = FieldValues.Parse(field, record[field.Name]); context.Values[field.Name] = value; context.Values[qualified] = value;
            }
            yield return context;
        }
    }
}
