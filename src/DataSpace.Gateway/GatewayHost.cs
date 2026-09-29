using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using DataSpace.Core;
using DataSpace.DataSources;
using DataSpace.DataSources.Relational;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace DataSpace.Gateway;

public sealed class SourceConnectionOptions
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public TableBinding[] Tables { get; set; } = [];
}

public static class GatewayHost
{
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5099");
        configure?.Invoke(builder);
        var secret = builder.Configuration["GatewayToken"] ?? "";
        if (secret.Length is < 32 or > 256) throw new InvalidOperationException("Configure GatewayToken with a random secret of 32–256 characters; never commit it.");
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var definitions = builder.Configuration.GetSection("SourceConnections").Get<SourceConnectionOptions[]>() ?? [];
        if (definitions.Length is < 1 or > 32) throw new InvalidOperationException("Configure 1–32 SourceConnections on the server.");
        var sources = new Dictionary<string, (GatewaySource Info, IDataSource Source)>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (definition.Id.Length is < 1 or > 64 || definition.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new InvalidOperationException("Source IDs must be ASCII letters, digits, '-' or '_'.");
            if (definition.ConnectionString.Length == 0) throw new InvalidOperationException("Every source requires a server-side connection string.");
            var source = CreateSource(definition);
            if (!sources.TryAdd(definition.Id, (new(definition.Id, definition.Name, definition.Provider), source))) throw new InvalidOperationException("Duplicate source ID.");
        }
        var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        foreach (var origin in origins)
        {
            var uri = SourceHttp.ValidateEndpoint(origin);
            if (uri.GetLeftPart(UriPartial.Authority) != origin.TrimEnd('/')) throw new InvalidOperationException("AllowedOrigins must contain exact origins, without paths, queries or wildcards.");
        }
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => { if (origins.Length > 0) policy.WithOrigins(origins).WithMethods("GET").WithHeaders("Authorization"); }));
        var app = builder.Build(); var slots = new SemaphoreSlim(8, 8);
        app.UseCors();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var auth = context.Request.Headers.Authorization.ToString();
            if (auth.Length > 512 || !auth.StartsWith("Bearer ", StringComparison.Ordinal) ||
                !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(auth[7..]))))
            { context.Response.StatusCode = 401; return; }
            if (!await slots.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted); deadline.CancelAfter(TimeSpan.FromSeconds(30));
                context.RequestAborted = deadline.Token;
                await next(context);
            }
            catch (OperationCanceledException) { if (!context.Response.HasStarted) context.Response.StatusCode = 408; }
            catch (DataSpaceException) { if (!context.Response.HasStarted) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Invalid source request or source size limit exceeded." }); } }
            catch (Exception error) when (error is DbException or InvalidOperationException or NotSupportedException)
            {
                // Do not log connection strings or propagate provider messages, which may contain credentials/host details.
                app.Logger.LogWarning("External provider operation failed ({ErrorType}).", error.GetType().Name);
                if (!context.Response.HasStarted) { context.Response.StatusCode = 502; await context.Response.WriteAsJsonAsync(new { error = "Source unavailable. Check server configuration and read-only permissions." }); }
            }
            finally { slots.Release(); }
        });
        app.MapGet("/v1/sources", () => sources.Values.Select(s => s.Info).ToArray());
        app.MapGet("/v1/sources/{id}/tables", async (string id, CancellationToken ct) => sources.TryGetValue(id, out var entry)
            ? Results.Json(await entry.Source.GetTablesAsync(ct), SourceLimits.Json) : Results.NotFound());
        app.MapGet("/v1/sources/{id}/rows", async (string id, string table, int? offset, int? limit, CancellationToken ct) => sources.TryGetValue(id, out var entry)
            ? Results.Json(await entry.Source.ReadAsync(new(table, offset ?? 0, limit ?? 200), ct), SourceLimits.Json) : Results.NotFound());
        app.Lifetime.ApplicationStopped.Register(() => { foreach (var entry in sources.Values) entry.Source.DisposeAsync().AsTask().GetAwaiter().GetResult(); });
        return app;
    }
    public static RelationalDataSource CreateSource(SourceConnectionOptions options)
    {
        switch (options.Provider.ToLowerInvariant())
        {
            case "postgresql":
                var postgres = NpgsqlDataSource.Create(options.ConnectionString);
                return new(options.Name, SourceDialect.PostgreSql, () => postgres.CreateConnection(), options.Tables, postgres.Dispose);
            case "mysql": case "mariadb":
                var mysql = new MySqlDataSource(options.ConnectionString);
                return new(options.Name, SourceDialect.MySql, () => mysql.CreateConnection(), options.Tables, mysql.Dispose);
            case "sqlserver":
                return new(options.Name, SourceDialect.SqlServer, () => new SqlConnection(options.ConnectionString), options.Tables);
            case "sqlite":
                var sqlite = new SqliteConnectionStringBuilder(options.ConnectionString) { Mode = SqliteOpenMode.ReadOnly, Pooling = false };
                return new(options.Name, SourceDialect.Sqlite, () => new SqliteConnection(sqlite.ToString()), options.Tables);
            default: throw new InvalidOperationException("Supported providers: postgresql, mysql, mariadb, sqlserver, sqlite.");
        }
    }
}
