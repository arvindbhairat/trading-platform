using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using StackExchange.Redis;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin connectivity check endpoint (GET /api/v1/admin/connectivity).
/// Verifies reachability and round-trip latency for MongoDB, PostgreSQL, and Redis.
/// All three checks run concurrently and errors for one do not affect others.
/// </summary>
public static class AdminConnectivityEndpoints
{
    public static IEndpointRouteBuilder MapAdminConnectivityEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        admin.MapGet("/connectivity", async (
            IMongoDatabase mongoDb,
            IConfiguration config) =>
        {
            var sw = Stopwatch.StartNew();

            // Fire all three checks concurrently.
            var mongoTask = CheckMongoAsync(mongoDb);
            var pgTask = CheckPostgreSqlAsync(config);
            var redisTask = CheckRedisAsync(config);

            await Task.WhenAll(mongoTask, pgTask, redisTask);

            return Results.Ok(new ConnectivityResponse(
                MongoDb: mongoTask.Result,
                PostgreSql: pgTask.Result,
                Redis: redisTask.Result
            ));
        }).RequireAuthorization();

        return app;
    }

    private static async Task<DatabaseConnectivity> CheckMongoAsync(IMongoDatabase database)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            sw.Stop();
            return new DatabaseConnectivity(true, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            return new DatabaseConnectivity(false, null, ex.Message);
        }
    }

    private static async Task<DatabaseConnectivity> CheckPostgreSqlAsync(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("SqlServer");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new DatabaseConnectivity(false, null, "PostgreSQL connection string is not configured");
        }

        try
        {
            var sw = Stopwatch.StartNew();
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync();
            sw.Stop();
            return new DatabaseConnectivity(true, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            return new DatabaseConnectivity(false, null, ex.Message);
        }
    }

    private static async Task<DatabaseConnectivity> CheckRedisAsync(IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Redis")
            ?? config["Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new DatabaseConnectivity(false, null, "Redis connection string is not configured");
        }

        try
        {
            var sw = Stopwatch.StartNew();
            await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
            var db = multiplexer.GetDatabase();
            await db.PingAsync();
            sw.Stop();
            return new DatabaseConnectivity(true, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            return new DatabaseConnectivity(false, null, ex.Message);
        }
    }
}

public sealed record ConnectivityResponse(
    DatabaseConnectivity MongoDb,
    DatabaseConnectivity PostgreSql,
    DatabaseConnectivity Redis
);

public sealed record DatabaseConnectivity(
    bool Reachable,
    long? LatencyMs,
    string? Error
);
