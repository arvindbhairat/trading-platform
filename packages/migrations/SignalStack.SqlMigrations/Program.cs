using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SignalStack.SqlMigrations;

/// <summary>
/// FluentMigrator runner for PostgreSQL migrations (REQ-MIGRATION-001/006/007).
///
/// Usage:
///   dotnet run -- --connection "Host=...;Database=marketdata;..." --database marketdata
///   dotnet run -- --connection "Host=...;Database=backtest;..." --database backtest
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
        // Databases are pre-provisioned (via docker init script or Azure).
        // PostgreSQL does not support CREATE DATABASE inside a transaction block,
        // so we connect directly to the target database.

        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
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
