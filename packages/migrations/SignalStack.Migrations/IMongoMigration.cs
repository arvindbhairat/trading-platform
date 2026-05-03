using MongoDB.Driver;

namespace SignalStack.Migrations;

/// <summary>
/// A single MongoDB migration.
///
/// REQ-MIGRATION-003: each migration must be either idempotent or paired
/// with an explicit rollback (<see cref="DownAsync"/>).  One-way migrations
/// (those that cannot provide a rollback) must be marked with
/// <see cref="OneWayMigrationAttribute"/>.
/// </summary>
public interface IMongoMigration
{
    /// <summary>
    /// Stable migration identifier (e.g. "M001", "M002").  Used as the primary
    /// key in the <c>_migrations</c> tracking collection and determines
    /// execution order (ascending).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Applies the migration forward.
    /// Should be idempotent where possible (use IF NOT EXISTS / upsert patterns).
    /// </summary>
    Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses the migration.
    ///
    /// Implement this method when a safe rollback path exists.  If the migration
    /// is naturally idempotent (re-running <see cref="UpAsync"/> produces no
    /// side-effects), this may be left as a no-op.
    ///
    /// If no safe rollback path exists, mark the class with
    /// <see cref="OneWayMigrationAttribute"/> and provide a justification.
    /// The default implementation throws <see cref="NotSupportedException"/>.
    /// </summary>
    Task DownAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"Migration {Id} does not support rollback. " +
            "If this is intentional, mark the class with [OneWayMigration] and provide a justification.");
}
