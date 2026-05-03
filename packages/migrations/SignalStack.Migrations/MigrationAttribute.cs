namespace SignalStack.Migrations;

/// <summary>
/// Marks a migration as one-way — i.e., it cannot be safely rolled back.
/// One-way migrations require explicit admin confirmation at deployment time
/// (REQ-MIGRATION-003).
/// </summary>
/// <remarks>
/// Apply this attribute to a migration class (FluentMigrator <c>Migration</c>
/// subclass or <see cref="IMongoMigration"/> implementation) whose
/// <c>Down()</c> / <c>DownAsync()</c> method either throws
/// <see cref="NotSupportedException"/> or performs a destructive operation
/// from which data cannot be recovered (destructive column drops, data
/// collapses, etc.).
///
/// Migrations that are naturally idempotent (e.g. <c>IF NOT EXISTS</c> DDL,
/// upsert-only data migrations) do NOT need this attribute — they are safe
/// by construction and can be re-run.  The linter treats them as implicitly
/// safe provided the code path is verified idempotent.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OneWayMigrationAttribute : Attribute
{
    /// <summary>
    /// Required justification for why this migration cannot provide a safe
    /// rollback path.  This text is surfaced to the admin at deployment time.
    /// </summary>
    public string Justification { get; }

    public OneWayMigrationAttribute(string justification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        Justification = justification;
    }
}
