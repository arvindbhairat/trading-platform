using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SignalStack.SqlMigrations;

/// <summary>
/// FluentMigrator runner for SQL Server migrations (REQ-MIGRATION-001/006/007).
///
/// Usage:
///   dotnet run -- --connection "Server=...;Database=marketdata;..." --database marketdata
///   dotnet run -- --connection "Server=...;Database=backtest;..." --database backtest
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var connectionString = GetArg(args, "--connection")
            ?? Environment.GetEnvironmentVariable("SQL_MIGRATION_CONNECTION");
        var databaseName = GetArg(args, "--database")
            ?? Environment.GetEnvironmentVariable("SQL_MIGRATION_DATABASE");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("FATAL: --connection argument or SQL_MIGRATION_CONNECTION env var is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            Console.Error.WriteLine("FATAL: --database argument or SQL_MIGRATION_DATABASE env var is required.");
            return 1;
        }

        try
        {
            await RunMigrationsAsync(connectionString, databaseName);
            Console.WriteLine($"Migrations completed successfully for database: {databaseName}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Migration failed for database {databaseName}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static async Task RunMigrationsAsync(string connectionString, string databaseName)
    {
        // Ensure the database exists.
        var builder = new SqlConnectionStringBuilder(connectionString);
        var masterConnection = builder.ConnectionString;
        builder.InitialCatalog = databaseName;

        await using (var conn = new SqlConnection(masterConnection))
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = @db)
                BEGIN
                    CREATE DATABASE [{databaseName}];
                    PRINT 'Created database: {databaseName}';
                END";
            cmd.Parameters.AddWithValue("@db", databaseName);
            await cmd.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddSqlServer()
                .WithGlobalConnectionString(builder.ConnectionString)
                .ScanIn(typeof(Program).Assembly).For.Migrations())
            .AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Information));

        await using var sp = services.BuildServiceProvider();
        var scope = sp.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();

        await Task.CompletedTask;
    }

    private static string? GetArg(string[] args, string prefix)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], prefix, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
