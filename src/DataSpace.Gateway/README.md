# DataSpace Gateway

A separately hosted, read-only HTTP adapter for server-configured PostgreSQL, MySQL/MariaDB, SQL Server and SQLite tables. The browser receives public source/table identifiers, not database connection strings, and imports local copies rather than editing remote data.

Configure the server, TLS, read-only database accounts, exact frontend origins and a strong session access token before use. The shared token authorizes all configured sources; this is not per-user authentication or role administration. No public gateway is deployed with the GitHub Pages frontend.

See [External data sources](../../docs/EXTERNAL-DATA.md) for configuration keys, adapter contracts, value preservation, limits, test scope and deployment boundaries. The project targets .NET 10 and is an application, not one of the reusable NuGet packages.
