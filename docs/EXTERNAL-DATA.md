# External data sources

DataSpace preview.4 provides read-only browsing and explicit local-copy import. SQLite runs locally; online databases use a separately hosted DataSpace gateway. GitHub Pages hosts only the frontend, not the gateway or a database service.

## Use the workspace

Choose **External Data → JSON File**, **SQLite**, **Online Database**, or **New Data Source**. The custom Uno dialog contains source selection, a table selector, a read-only Skia preview, page navigation, refresh, cancellation and import options.

JSON inputs must contain an array of objects. JSON Pointer can select a nested array, for example `/data/items`. Escape `/` inside a property name as `~1` and `~` as `~0`. Array indices are zero-based without leading zeroes.

The preview requests 200 records per page and caches four pages by default. Refresh invalidates the preview cache. Choose a unique local table name and a maximum record count, then select **Import table**. The source is read into a detached draft and added to the workspace in one validated, undoable transaction. A cancelled or invalid import does not add a partial table. Save the workspace afterward.

**Imports are copies, not linked tables.** Editing a copy never updates the source. Refresh updates the preview, not an existing imported table. Independent offset-based requests do not provide a transaction spanning all pages: use a stable unique ordering key and avoid concurrent source changes during an import.

## Adapters and reusable components

| Adapter | Implementation | Important boundary |
| --- | --- | --- |
| JSON file or URL | Portable `DataSpace.DataSources` | Parses and validates the complete document once; requested rows are materialized on demand. |
| SQLite file on desktop | `DataSpace.DataSources.Relational`, Microsoft.Data.Sqlite | Opens read-only and moves SQLite work off the UI thread. |
| SQLite file in browser | Dedicated worker and pinned sql.js WASM | Opens a disposable in-memory file. No remote upload, OPFS persistence or direct editing of the original file. |
| PostgreSQL | Gateway using Npgsql | Reads only server-configured tables. |
| MySQL / MariaDB | Gateway using MySqlConnector | Shared adapter; live CI covers MariaDB, not a separate MySQL server. |
| SQL Server | Gateway using Microsoft.Data.SqlClient | Ordered, parameterized paging of configured tables. |
| SQLite through gateway | Gateway using Microsoft.Data.Sqlite | Exposes configured tables through the same HTTP protocol. |

`IDataSource` supplies catalog/read methods and asynchronous disposal. `SourcePager` adds a bounded cache and does not own the source lifetime. `SourceImport` produces a detached local table. `ExternalDataControl` can be hosted independently of the application shell. The native relational package is excluded from the browser dependency graph. External wire records use generated JSON metadata for the trimmed WebAssembly build.

## Gateway configuration

Run `DataSpace.Gateway` on infrastructure you control. The source project and its README are part of the repository; the Pages deployment does not start it. The framework-dependent server requires .NET 10. Deployment-specific configuration belongs on the server, not in frontend assets, exported documents or source control.

The configuration keys are:

| Key | Purpose |
| --- | --- |
| `GatewayToken` | A strong random 32–256-character session access secret, supplied securely by the operator. |
| `AllowedOrigins` | Exact frontend origins, without paths, query strings or wildcards. |
| `SourceConnections` | An array of server-side source definitions. |
| `Id`, `Name`, `Provider` | Public identifier, display name and adapter name for each source. |
| `ConnectionString` | Server-only credentials and provider settings for a least-privilege database account. |
| `Tables` | Explicit allowlist of exposed table bindings. |
| Table `Id`, `Name`, `Schema`, `OrderColumns` | Public table ID, database identifier/schema and stable unique ordering key. |

Provider names are `postgresql`, `mysql`, `mariadb`, `sqlserver` and `sqlite`. A composite ordering key uses multiple `OrderColumns`. Configure the database account with read permissions only; the adapter is not a substitute for database authorization.

In the UI, enter the HTTPS gateway base address and the session access token. The application never asks for an online database connection string. The token is not saved in the workspace and is cleared when the dialog/source is disposed. A JSON URL request is separate and receives no gateway token.

**One gateway token authorizes all sources configured in that gateway.** This is not per-user authentication, role administration or per-table user authorization. Operators must supply TLS with certificate validation, secure configuration, token rotation, network controls and least-privilege accounts. Plain HTTP is accepted by the client only for loopback development. Public-to-localhost browser restrictions may still apply. Use a properly hosted HTTPS endpoint for normal use. Redirects and embedded URL credentials are rejected.

The read-only protocol exposes three GET endpoints: `/v1/sources`, `/v1/sources/{id}/tables`, and `/v1/sources/{id}/rows` with `table`, `offset` and `limit` parameters. Clients send configured IDs, not SQL or database credentials. Table identifiers are quoted per provider; paging values are bound parameters. Catalog metadata is briefly cached; response, concurrency and execution limits are enforced. Provider error details are not returned to the browser.

## Values and limits

Relational values are imported as text/null to avoid implicit narrowing of database-specific types. Binary data is represented as `hex:` plus uppercase hexadecimal. JSON signed 64-bit integers and booleans are typed; non-integer numbers and nested arrays/objects remain their JSON text. Missing properties become null and empty strings remain empty. Local field names are sanitized, retaining original captions.

SQLite reserves table names beginning with `sqlite_`, including case variations. Export prefixes those names with `DataSpace_` (for example `SQLite_Items` becomes `DataSpace_SQLite_Items`) and reports the resulting table name in the status bar. The local DataSpace table and exported file name are unchanged. Names such as `sqliteX` are not reserved and remain unchanged.

SQLite export creates a separate database with TEXT/null columns. It does not preserve source types, indexes, constraints, triggers, views, relationships or Access objects. This is data interchange, not a byte-identical database backup. Values already stored as SQLite REAL have the precision of that stored representation.

The limits are 16 MiB per source file, 100,000 imported records, 128 columns, 1,000 rows per protocol page, offset up to 1,000,000, and one million characters per cell. Page contents also have a cumulative character limit and HTTP byte limits. The UI requests 200 rows per page. JSON needs at least one discoverable column. SQLite supports up to 128 user tables. Preview caches hold 1–16 pages, default four.

Only SQLite execution moves to a browser worker. JSON parsing, complete imports and local managed SQL can still occupy the browser UI thread. The gateway is not independently security-audited or production-scale qualified.

## Verification and measurement

The source tests cover JSON, real native SQLite, gateway authorization and page contracts. The live service job covers PostgreSQL, MariaDB and SQL Server. A separate MySQL version is not yet qualified. The browser suite exercises actual Uno file selection, import/export, SQLite worker operations, HTTP adapters and persistence; results must be read from the exact commit's CI run.

The existing `DataSpace.Benchmarks` program includes a warm JSON page-read comparison. Both paths use already-parsed data and return an identical late page. The reference repeats JSON array indexing and per-column lookup; the optimized path indexes small row handles and enumerates each object's properties once. Parsing, schema scanning, handle-index construction, I/O and rendering are excluded from its timing. The handle index adds storage proportional to the row count. Separate assertions check that cache hits cause no additional row decoding. These measurements do not establish whole-application, startup, browser or GPU speedups.
