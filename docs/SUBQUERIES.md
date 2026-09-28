# Subqueries and Find query builders

DataSpace supports nested SELECT queries in expressions. This extends the managed dialect; it does not establish complete ACE/Jet SQL compatibility.

## Supported forms

```sql
-- A scalar aggregate is independent of each outer record.
SELECT ID, Company
FROM Customers
WHERE ID > (SELECT AVG(ID) FROM Customers);

-- Correlated existence uses the current outer record.
SELECT c.ID, c.Company
FROM Customers AS c
WHERE EXISTS (
  SELECT o.ID FROM Orders AS o WHERE o.[Customer ID] = c.ID
);

-- An inner NULL affects NOT IN unless it is explicitly excluded.
SELECT c.ID
FROM Customers AS c
WHERE c.ID NOT IN (
  SELECT o.[Customer ID] FROM Orders AS o
  WHERE o.[Customer ID] IS NOT NULL
);

-- ANY and SOME are synonyms. ALL checks every inner value.
SELECT ID FROM Customers WHERE ID > ALL (SELECT [Customer ID] FROM Orders);

-- Inner aggregates do not turn the outer SELECT into a grouped query.
SELECT c.ID,
  (SELECT COUNT(*) FROM Orders AS o WHERE o.[Customer ID] = c.ID) AS OrderCount
FROM Customers AS c ORDER BY c.ID;
```

Scalar subqueries return one column and at most one row. An empty scalar result is NULL; multiple rows are an error. IN, ANY/SOME and ALL also require one column. EXISTS tests whether any row exists, regardless of its projected value. A plain EXISTS can stop after its first qualifying row; aggregate/HAVING, DISTINCT/OFFSET and other cardinality rules remain significant.

ANY over an empty set is false; ALL over an empty set is true. NULL comparisons produce unknown unless another comparison determines the result. A matching value makes IN true even if another inner value is NULL. Without a match, an inner NULL makes IN/NOT IN unknown. NULL IN an empty set is false. WHERE retains true predicates only.

Nested SELECT may contain supported UNION branches. Nested action statements, SELECT INTO and multiple statements are rejected. Invalid nested names and column counts are bound even when an outer source is empty. Scalar cardinality is evaluated when that expression is reached. Explicit parameters use `@name`; existing implicit parameter fallback remains available.

## Scope and actions

A field resolves in its closest query scope before outer scopes. Qualify ambiguous fields. Reusing an alias in an inner query hides the outer alias; a missing field on that inner alias does not silently resolve to the outer table. Multi-level correlations retain the referenced outer fields through scan pruning. Correlated outer values used by a grouped outer projection must be grouped appropriately.

UPDATE, DELETE and INSERT VALUES evaluate their nested reads before applying the statement's writes. For example, assigning `(SELECT MAX(Value) FROM T)+1` to several rows observes the same pre-statement T, not each preceding update. Failed scalar cardinality or validation rolls back the workspace transaction. These semantics apply to the managed document, not an external database transaction service.

Subqueries require a database query context. The standalone ExpressionEvaluator and table-view filter API do not execute nested database reads. Derived tables in FROM, lateral sources, nested TRANSFORM syntax, the full Access PARAMETERS/coercion/collation rules, joined updateability and full query-plan optimization are not implemented.

## Reusable Find builders

Open a query and choose **Find Duplicates** or **Find Unmatched**. Select the source and keys, then **Generate SQL**. Generation does not run the query or modify records. Cancel leaves SQL unchanged; Run previews ordinary read-only query results. Save retains that SQL with the query.

`FindQueryDesign` belongs to DataSpace.Query and has no Uno dependency. `FindQueryBuilderControl` belongs to DataSpace.Controls and can be hosted independently. Definitions validate source/key/output names and compatible related-key types before emitting quoted SQL.

Duplicate summary groups selected keys and returns a count. Detail mode returns each duplicate record, with selectable output fields; it does not require a primary key. Including duplicate NULL keys is explicit. Single-key details use a grouped independent set of duplicate non-null keys and a separate null count when needed, allowing the existing per-execution cache and compatible membership index to avoid correlated scans. Composite-key details retain null-safe correlated counts and may reuse admitted repeated key tuples. Cache and query work limits still apply; this is query authoring, not persistent indexing or arbitrary-query decorrelation.

Single-key unmatched queries explicitly retain NULL source keys and exclude NULL inner keys before a NOT IN test. Their independent inner set can be cached and indexed. Composite unmatched keys use a LEFT JOIN with a nonmatching-row test, retaining hash-join eligibility and avoiding duplicate unmatched rows. The builders author table sources, not arbitrary external/derived sources. They save ordinary SQL, not a separate restorable wizard-state format.

## Performance, limits and isolation

Each execution owns its subquery bindings, results, parameters and work counters. The shared parser cache holds syntax only. A deterministic uncorrelated subquery may be evaluated once and reused within that execution. Deterministic correlated subqueries may reuse results when their bound outer-reference tuple repeats. Time-dependent subqueries are not result-cached. Queries depending on saved sources are conservatively excluded from memoization until volatility can be propagated without executing those sources.

Eligible IN sets use numeric, case-insensitive text or DateTime membership indexes only when the outer operand has the same comparison family. Coercing comparisons fall back to the existing comparator rather than changing equality semantics. These are transient lookup sets, not persistent storage indexes.

QueryOptions exposes `EnableSubqueryCache`, `EnableMembershipIndexes`, `MaximumSubqueryDepth` (16), `MaximumSubqueryExecutions` (10,000), `MaximumTotalSourceRows` (10,000,000), and `MaximumSubqueryCacheBytes` (8 MiB admission estimate). The existing intermediate/result limits still apply; linear subquery comparisons share a budget of MaximumIntermediateRows × 16. Independent and correlated entries share a 128-entry cap per scope. Correlation keys also count against the execution-wide admission budget; oversized keys and uncached tuples fall back to normal evaluation. Admission estimates include a conservative allowance for membership storage; they are not measured process memory.

QueryStatistics reports subquery executions, cache hits, comparisons, membership probes, peak depth and estimated admitted cache bytes. Cancellation and work limits cover nested execution, but execution is still synchronous in the browser: this is not a worker or cooperative UI scheduler. Correlations can still rescan source tables. Full-document saves, action validation and other documented scaling limits remain.

### Repeated-correlation cache

Cache keys use coordinates established by lexical binding (scope depth and field name), not an unqualified-name lookup against a later row. Every referenced outer value participates. Keys preserve text case, null versus empty strings, tuple boundaries, decimal scale, numeric type and DateTime representation. This identity is deliberately stricter than SQL comparison: string conversion can observe differences that a case-insensitive join would ignore. Nested aliases and multi-level outer references remain scoped.

The cached object is the inner result, never the truth value of an outer comparison. IN/ANY/ALL still evaluate the current outer operand. Errors are not cached; action queries still read pre-statement data, and all caches are discarded between executions/documents/parameter sets. Volatile and saved-source subqueries remain conservatively uncached. Byte and entry limits stop admission, not correctness. This is bounded result memoization, not general decorrelation, persistent indexing or asynchronous browser execution.

`SubqueryCacheHits` includes both independent and repeated-correlation hits. Disable `EnableSubqueryCache` to compare against re-evaluation. The repeated-correlation benchmark has no independent nested expressions, so that switch isolates this optimization. The benchmark includes 500 outer rows, 250 inner rows and eight repeated keys, verifies identical results and checks 500 versus eight inner executions before recording timings.
