using MongoDB.Driver;
using SignalStack.Migrations;
using Xunit;

namespace SignalStack.Migrations.Tests;

/// <summary>
/// Tests for <see cref="MigrationLinter"/> validation rules (REQ-MIGRATION-003).
/// Uses inline IMongoMigration implementations to verify each rule path.
/// </summary>
public sealed class MigrationLinterTests
{
    // ── Rule A: [OneWayMigration] with justification ─────────────────────────

    [Fact]
    public void OneWayMigration_with_justification_passes()
    {
        var result = MigrationLinter.ValidateMigration(typeof(OneWayWithJustification));
        Assert.Null(result);
    }

    [Fact]
    public void OneWayMigrationAttribute_rejects_empty_justification()
    {
        // The attribute constructor itself rejects empty/whitespace justifications,
        // so the linter never encounters this case.
        Assert.Throws<ArgumentException>(() => new OneWayMigrationAttribute(""));
        Assert.Throws<ArgumentException>(() => new OneWayMigrationAttribute("   "));
    }

    // ── Rule B: explicit rollback method ─────────────────────────────────────

    [Fact]
    public void Migration_with_explicit_rollback_passes()
    {
        var result = MigrationLinter.ValidateMigration(typeof(WithExplicitRollback));
        Assert.Null(result);
    }

    [Fact]
    public void Migration_without_rollback_and_without_attribute_fails()
    {
        var result = MigrationLinter.ValidateMigration(typeof(WithoutRollback));
        Assert.NotNull(result);
        Assert.Contains("no rollback method", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Assembly-level scan ──────────────────────────────────────────────────

    [Fact]
    public void ValidateAll_returns_errors_for_invalid_migrations()
    {
        // Scan the test assembly — it includes all inline types defined here,
        // including WithoutRollback which should fail.
        var errors = MigrationLinter.ValidateAll([typeof(MigrationLinterTests).Assembly]);

        // WithoutRollback should be caught.
        Assert.Contains(errors, e => e.Contains(nameof(WithoutRollback)));
    }

    // ── Inline test migrations ───────────────────────────────────────────────

    [OneWayMigration("Destructive column drop that cannot be reversed.")]
    private sealed class OneWayWithJustification : StubMigration
    {
        public override string Id => "TEST-ONEWAY-VALID";
        public override Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Has an explicit DownAsync override — valid under Rule B.
    /// </summary>
    private sealed class WithExplicitRollback : StubMigration
    {
        public override string Id => "TEST-ROLLBACK";
        public override Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        // DownAsync is overridden (non-throwing no-op).
        public override Task DownAsync(IMongoDatabase database, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// No DownAsync override, no [OneWayMigration] — should fail.
    /// </summary>
    private sealed class WithoutRollback : StubMigration
    {
        public override string Id => "TEST-NO-ROLLBACK";
        public override Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Abstract stub that implements IMongoMigration with a default DownAsync
    /// that throws (matching the interface's default implementation).
    /// Derived classes that don't override DownAsync inherit the throw — the
    /// linter should flag them.
    /// </summary>
    private abstract class StubMigration : IMongoMigration
    {
        public abstract string Id { get; }

        public abstract Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default);

        public virtual Task DownAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("No rollback supported.");
    }
}
