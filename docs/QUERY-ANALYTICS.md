# Crosstabs and make-table queries

## Crosstab authoring

Open or create a query and choose **Crosstab Builder**. Select a table, one or more row-heading fields, the column-heading expression, the value expression and aggregate. Optional fixed headings keep columns present even when no rows match them; leave the list empty to discover columns. **Include row totals** adds the same aggregate across all matching source rows.

**Generate SQL** explicitly replaces the editor contents; **Cancel** leaves them unchanged. Run previews the result and Save persists the SQL. Reopening the builder restores its own single-table, ascending-row-order SQL. Advanced joins, HAVING, source aliases and custom row headings/order remain SQL-only; the builder starts a new draft for those queries rather than claiming to import an equivalent design.

```sql
TRANSFORM Sum([Amount])
SELECT [Customer ID], Sum([Amount]) AS [Row Total]
FROM [Orders]
GROUP BY [Customer ID]
ORDER BY [Customer ID]
PIVOT [Status] IN ('New', 'Processing', 'Shipped');
```

The executor also supports the existing joins, WHERE/HAVING, supported scalar expressions, saved read-only sources and supplied parameters in crosstabs. A saved crosstab can itself be a read-only source for SELECT or a report. The authoring model (`CrosstabDesign`) belongs to Query and has no Uno dependency; `CrosstabBuilderControl` is independently embeddable.

```csharp
var definition = new CrosstabDesign
{
    Source = "Orders", RowFields = ["Customer ID"],
    ColumnExpression = "Status", ValueExpression = "Amount",
    Aggregate = CrosstabAggregate.Sum,
    FixedHeadings = "'New', 'Processing', 'Shipped'", ShowRowTotals = true
};
string sql = definition.ToSql(workspace.Document);
var result = new QueryEngine().Select(workspace.Document, sql);
```

### Explicit dialect limits

TRANSFORM accepts one SUM, COUNT, AVG, MIN, MAX, FIRST or LAST aggregate. FROM and GROUP BY are required. DISTINCT, TOP/LIMIT/OFFSET and INTO are not accepted inside TRANSFORM. ORDER BY sorts row headings/aggregates, not dynamically generated pivot columns. The SELECT QBE grid does not visually edit crosstabs; use the dedicated builder or SQL View.

Fixed headings preserve declaration order; dynamic headings use typed ascending order. Duplicate/ambiguous heading labels fail instead of merging different columns. Null pivot values do not create a column but still contribute to row totals. Missing cells are null, including missing COUNT cells; an existing cell whose values are all null has COUNT(value)=0. FIRST/LAST currently preserve the engine's non-null selection semantics, not full Access behavior. Row totals include values excluded from a fixed heading list.

`MaximumCrosstabColumns` defaults to 256 including row headings; `MaximumCrosstabCells` defaults to 250,000 and bounds occupied accumulators and dense output cells. Source, result and cancellation limits also apply. Parameters are supplied through the existing parameter dictionary/editor; SQL PARAMETERS declarations and full Access coercion/format/collation compatibility are not implemented.

## Make-table queries

```sql
SELECT TOP 10 [ID], [Company], [Country]
INTO [Customer Archive]
FROM [Customers]
ORDER BY [Company];
```

`QueryEngine.Execute` creates a new local table inside one validated, undoable transaction. An existing table/query name, invalid target schema, evaluation failure or cancellation leaves the workspace unchanged. Direct source columns retain their type and field size, but required/default/unique/index/relationship settings are not copied. Record identities are new. Empty selections still create their supported result schema.

This does not export an Access file or write to an external database. INTO is not accepted inside UNION, INSERT SELECT or a read-only saved source. The UI requires action confirmation before execution; `Select` rejects action statements.

## Index DDL

```sql
CREATE UNIQUE INDEX [CountryCompany] ON [Customers] ([Country], [Company]);
DROP INDEX [CountryCompany] ON [Customers];
```

Index DDL uses the same validated constraint metadata as the Indexes editor. Existing duplicate data rejects a unique index atomically. ASC is accepted; DESC fails explicitly because physical descending keys are not implemented. Indexes do not yet provide persistent B-tree query execution. PRIMARY/DISALLOW NULL/IGNORE NULL clauses and external backend DDL are not supported.

## Reference semantics

Syntax and direct make-table field inheritance were checked against Microsoft's [TRANSFORM reference](https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/transform-statement-microsoft-access-sql) and [SELECT INTO reference](https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/select-into-statement-microsoft-access-sql). The limitations above describe DataSpace, not a certification of complete Microsoft Access SQL equivalence.
