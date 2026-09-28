# Performance and measurement

## Implemented execution paths

Ordinary cell/range edits use `DatabaseWorkspace.UpdateRecords`. It copies document metadata and record-list containers, but copies only touched record dictionaries. It normalizes changed cells and rechecks affected unique indexes and relationships. Cascading parent-key changes fall back to a fully detached transaction. General schema/action edits use a direct model snapshot rather than a serialize/deserialize round trip. All paths retain validation, revision guards and atomic undo.

`TableView.Open` exposes a snapshot-backed `VirtualTableView`. Opening an unfiltered, unsorted table does not clone its records. The view keeps at most 256 detached display records in a FIFO cache, and `ReadPage` materializes a requested range. Filtering builds row indices, reuses one scalar environment and parses referenced fields only. Sorting caches typed sort keys. `TableView.Select` remains the eager compatibility API.

The datasheet uses the lazy API, identity lookup without `ToList`, and targeted cell transactions. Navigation and ribbon tabs no longer rebuild on every cell edit. Search is debounced. Totals are computed in one pass when requested and cached until the snapshot changes; repainting or scrolling does not rescan the table.

SELECT pipelines stream ordinary projections and filters. Without ORDER BY, TOP/LIMIT stops source enumeration once enough output rows have been produced. GROUP BY retains an accumulator set per group instead of all source rows. Single-table streaming scans reuse one scalar context; the original buffered-group path and join inputs deliberately keep distinct contexts. Ordered TOP/LIMIT uses a stable priority queue retaining at most offset+limit candidates; the source is still scanned. Unbounded ORDER BY still requires a full bounded sort. Compatible qualified equality joins build a transient hash lookup of the right input. Duplicate keys, residual ON predicates, composite equality keys and LEFT unmatched rows are preserved. Ineligible or coercing joins use the bounded nested-loop path. `QueryStatistics` reports source-row reads, candidate comparisons and selected join strategy.

Single-source SELECT and TRANSFORM scans also prune unused fields. Every expression is bound against the complete schema before choosing the physical scan. Projections, filters, grouping, sorting, HAVING, pivot expressions and aggregate arguments contribute their referenced fields. Expanded wildcards retain all fields. Explicit parameters do not require source values; unqualified names retain the existing field-before-parameter resolution. Output aliases may conservatively retain an extra field rather than risk dropping a required input. Join inputs still use complete independent contexts. `COUNT(*)` can scan/count records without decoding any typed field values, but still observes cancellation and row-work limits. Correlated subquery references also retain their required outer fields.

Eligible independent subqueries cache their result values within one execution. Repeated deterministic correlated tuples may also reuse inner results using exact bound outer-reference keys. Time-dependent and saved-source-dependent subqueries remain uncached. IN can index a homogeneous set with the same comparison family as the outer value; coercing comparisons keep the reference comparator. Caches are not shared across executions or documents. These are transient lookup sets, not persistent database indexes. The index designer manages constraint metadata; a durable page store, persistent B-trees and a cost-based planner remain unimplemented.

## Reproduce

```bash
dotnet test tests/DataSpace.Tests/DataSpace.Tests.csproj -c Release
dotnet run --project benchmarks/DataSpace.Benchmarks/DataSpace.Benchmarks.csproj \
  -c Release -- artifacts/performance.json
```

CI runs this program after the managed regression suite and retains `performance-results`. It performs warmups, alternates baseline/optimized measurement order, and reports medians, raw samples and per-thread allocated bytes. It deliberately does not gate correctness on unstable wall-clock thresholds. Unit tests instead check identities, rollback, bounded materialization, result equivalence and candidate-work counts.

## Recorded engine sample

Run [36336033507](https://github.com/wieslawsoltes/DataSpace/actions/runs/36336033507), source `62dfd49281fe51620d54a719f6322408cc8a85e0`, measured on 2026-09-27 with .NET 10.0.12, Ubuntu 24.04.5, x64, four logical processors, Release:

| Scenario | Baseline median | Optimized median | Baseline allocated | Optimized allocated |
| --- | ---: | ---: | ---: | ---: |
| One cell edit, 25,000 two-field records | 171.09 ms | 1.68 ms | 56,704,336 B | 2,163,648 B |
| Open 25,000 rows, display first 50 | 31.29 ms | 0.11 ms | 10,625,608 B | 27,264 B |
| 1,000 × 1,000 equality join, 1,000 output rows | 536.02 ms | 3.64 ms | 867,182,872 B | 4,900,032 B |

The edit baseline reproduces the former JSON clone, mutation and full validation sequence. The view baseline materializes the whole detached view; the optimized case opens the view and obtains 50 records. The join baseline is the same executor with hash lookup disabled, not a different database product. The join comparisons fall from 1,000,000 to 1,000 with identical output.

**Scope:** these are managed-engine microbenchmarks, not browser startup, end-to-end interaction latency, file-save timing, frame rate, peak live memory or hardware-GPU measurements. Allocation figures measure total managed allocations during an operation, not retained memory. Results vary by environment, record width, constraints, joins and data distribution. Do not extrapolate these ratios to the whole application.

## Grouping and ordered TOP sample

Run [36346385269](https://github.com/wieslawsoltes/DataSpace/actions/runs/36346385269), source `b90b3134b0e203c81c982602423c386b2488c875`, on 2026-09-27 with .NET 10.0.12 / Ubuntu 24.04.5 / x64 / four logical processors, Release:

| Scenario | Reference median | Optimized median | Reference allocated | Optimized allocated |
| --- | ---: | ---: | ---: | ---: |
| 50,000 rows, 32 groups, five aggregates | 119.7956 ms | 47.7650 ms | 94,947,016 B | 35,235,048 B |
| Ordered TOP 20 over 50,000 rows | 146.0129 ms | 51.2210 ms | 115,868,568 B | 59,622,240 B |

The first reference retains 50,000 contexts and scans each group's input for every aggregate; the optimized query retains 32 group states and zero grouped input rows. The second reference sorts 50,000 candidates; the optimized query keeps only 20. Both scan all 50,000 source rows, and both pairs verify identical output before measuring. The optimized cases also enable scalar-context reuse, so these are combined execution-path improvements rather than isolated algorithm timings.

`EnableStreamingAggregates`, `EnableTopKSort` and `EnableReusableRowContexts` independently select the reference/optimized paths. Statistics report buffered group rows, retained groups, source contexts, sort candidates and peak sort rows. These counters describe executor state, not measured process peak memory. Five alternating samples after two warmups are recorded; timings remain diagnostic, not CI pass/fail thresholds or browser responsiveness claims.

## Wide-table scan benchmark

The benchmark constructs 10,000 records with 64 decimal columns and runs:

```sql
SELECT TOP 20 F0 FROM Wide WHERE F1 >= 0 ORDER BY F0 DESC;
```

The paired engines differ only in `QueryOptions.EnableColumnPruning`; both use reusable scalar contexts and the same bounded TOP selection. The comparison isolates column decoding and the smaller per-row context. It checks identical output and exact work counts before timing: 640,000 typed source values without pruning, 20,000 with pruning. Both scan all 10,000 records. These counts measure avoided decoding, not a claimed wall-clock speedup; use the generated artifact for the actual timings on each runner.

`QueryStatistics.SourceValuesRead` counts typed source values decoded during execution, excluding schema binding and output serialization. Saved-query sources contribute their own execution work. Disabling pruning is useful for differential tests; it does not select a different storage backend. `ColumnPruningTests` compares projections, aliases, parameters, hidden filter/sort inputs, wildcards, grouping, crosstabs, joins, saved sources and make-table results across pruning/context/aggregation options. Unknown names are rejected even on empty sources and TOP 0.

## Independent subquery and membership benchmarks

Two additional comparisons use Candidates with IDs 1–500 and Allowed with IDs 1–250:

```sql
SELECT ID, (SELECT MAX(ID) FROM Allowed) AS Highest FROM Candidates;
SELECT ID FROM Candidates WHERE ID IN (SELECT ID FROM Allowed);
```

The first changes only `EnableSubqueryCache`. The reference performs 500 inner executions and reads 125,500 total source records. The cached execution performs one inner query and reads 750 records. The second keeps result caching enabled on both engines and changes only `EnableMembershipIndexes`: the linear reference performs 93,875 comparisons, while the indexed execution performs 500 membership probes with no linear comparisons. Both pairs check identical output and these exact work counts before timing.

These are comparisons against switchable reference paths in the same implementation, not a prior release that lacked subquery support. Caching avoids repeated execution; hashing trades extra transient set storage for fewer comparisons and may allocate more than a cached linear scan. The benchmark reports this allocation tradeoff rather than implying every optimization reduces memory. Warmups, alternating samples and JSON output follow the same measurement procedure above.

Read [subquery contracts and limits](SUBQUERIES.md) for cache admission estimates, lexical scope, volatile/saved-source exclusions and repeated-correlation keys and execution-wide row/comparison/depth limits. Unknown names in an unvisited subquery still fail binding; the optimized path must not hide missing fields or unsafe action statements.

## Remaining scaling limits

Record-list copying and identity maps still scale with table size. Filters/sorts remain scans unless TOP can stop a streamed SQL pipeline. Groups retain accumulator state per distinct group; highly distinct grouping still grows with input. UNION, saved-query sources and unbounded ordering buffer their results, and DISTINCT keeps a seen-key set. TOP with a large offset may fall back to full sorting. Crosstab pivot cells and dense output are separately bounded. Correlated subqueries and duplicate-detail searches can rescan per outer record; they are not automatically decorrelated. The new lexical-scope metadata also adds per-context overhead, so improvements in nested-query scenarios do not imply every existing query is faster.

Undo/redo deep-copy a restored snapshot. Complex cascades and action queries retain full-document validation. Saves still serialize the whole document, and browser storage/import retains its size limit. WebAssembly threads are disabled in the Pages build; CPU-heavy operations can still block browser input. No hardware-GPU, million-row end-to-end, native OS or multi-user performance qualification is claimed.

All public model objects remain mutable for construction/serialization. Change live data through the workspace transaction API only. A virtual view's returned records are detached display snapshots with a bounded lifetime, not writable backing storage. External direct mutation can bypass constraints, invalidate caches and violate snapshot/history assumptions.

## Repeated correlated subqueries

Deterministic repeated outer tuples can reuse an inner result within one query execution. The key encodes exact bound outer values, including lexical scope, case and decimal representation, and the shared admission budget includes key storage. Independent and correlated entries share a 128-entry per-scope limit. New tuples beyond the limit use the reference evaluator; existing admitted tuples remain reusable. Volatile/saved-source subqueries are excluded. See SUBQUERIES.md for semantics and limits.

The `Repeated correlation / 500 outer / 250 inner / 8 keys` benchmark switches only `EnableSubqueryCache` and contains no independent nested expressions. It asserts identical rows, 500 versus eight subquery executions, and 125,500 versus 2,500 total input reads. Timings, raw samples, total allocation counts and the runner environment are written to the existing performance artifact; those counts are not whole-app latency or peak-memory measurements. Correlations with mostly distinct tuples remain scan-bound.
