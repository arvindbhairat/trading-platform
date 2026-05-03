using System.Reflection;

namespace SignalStack.Migrations;

/// <summary>
/// Linter that validates migration classes against REQ-MIGRATION-003 rules.
///
/// Each migration must satisfy at least one of:
///   A. Be marked with <see cref="OneWayMigrationAttribute"/> with a non-empty
///      justification.  One-way migrations require explicit admin confirmation
///      at deployment time (checked by the deployment pipeline, not this linter).
///   B. Have an explicit rollback path — an overridden <c>Down()</c> method
///      (FluentMigrator) or an overridden non-throwing <c>DownAsync()</c>
///      method (<see cref="IMongoMigration"/>).
///
/// Migrations that are idempotent by construction (e.g. IF NOT EXISTS DDL,
/// upsert-only data migrations) should provide a no-op rollback method and
/// document their idempotency in the class XML doc.
/// </summary>
public static class MigrationLinter
{
    /// <summary>
    /// Validates all discovered migration types in the given assemblies against
    /// the REQ-MIGRATION-003 rules.
    /// </summary>
    /// <param name="assemblies">Assemblies to scan for migration types.</param>
    /// <returns>A list of validation errors.  Empty if all migrations pass.</returns>
    public static IReadOnlyList<string> ValidateAll(IEnumerable<Assembly> assemblies)
    {
        var errors = new List<string>();

        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => IsMigrationType(t) && !t.IsAbstract && !t.IsInterface);

            foreach (var type in types)
            {
                var error = ValidateMigration(type);
                if (error is not null)
                    errors.Add(error);
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates a single migration type.
    /// Returns <c>null</c> if the migration passes all rules.
    /// </summary>
    public static string? ValidateMigration(Type migrationType)
    {
        // ── Rule A: [OneWayMigration] with justification ───────────────────
        var oneWayAttr = migrationType.GetCustomAttribute<OneWayMigrationAttribute>();
        if (oneWayAttr is not null)
        {
            if (string.IsNullOrWhiteSpace(oneWayAttr.Justification))
            {
                return $"[{migrationType.FullName}] is marked [OneWayMigration] but has an empty justification. " +
                       "Provide a non-empty justification explaining why no safe rollback path exists.";
            }
            return null; // Valid — admin confirmation required at deploy time.
        }

        // ── Rule B: explicit rollback method ───────────────────────────────
        if (HasRollbackMethod(migrationType))
            return null;

        // ── Does not satisfy either rule ───────────────────────────────────
        return $"[{migrationType.FullName}] is not marked [OneWayMigration] and has no rollback method. " +
               "Each migration must be paired with an explicit rollback or marked " +
               "[OneWayMigration] with a justification (REQ-MIGRATION-003). " +
               "Idempotent migrations should provide a no-op rollback method.";
    }

    private static bool IsMigrationType(Type type)
    {
        // FluentMigrator migrations extend the Migration base class.
        if (IsMigrationBaseSubclass(type))
            return true;

        // Our custom IMongoMigration interface.
        if (typeof(IMongoMigration).IsAssignableFrom(type))
            return true;

        return false;
    }

    /// <summary>
    /// Checks for a non-throwing rollback method.
    ///
    /// For FluentMigrator: <c>Down()</c> is public virtual in the base
    /// <c>Migration</c> class.  A migration that overrides it with any
    /// implementation (including a no-op) is considered to have a rollback
    /// path.  A migration that does not override <c>Down()</c> inherits
    /// the base class's no-op implementation, which is also safe — but
    /// the linter requires an explicit override to confirm intent.
    ///
    /// For IMongoMigration: <c>DownAsync</c> has a default implementation
    /// that throws <c>NotSupportedException</c>.  A class that overrides
    /// it (even with a no-op) is considered to have a rollback path.
    /// </summary>
    private static bool HasRollbackMethod(Type type)
    {
        // Check for FluentMigrator Down() override.
        if (IsMigrationBaseSubclass(type))
        {
            var downMethod = type.GetMethod("Down",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            return downMethod is not null;
        }

        // Check for IMongoMigration DownAsync() override.
        if (typeof(IMongoMigration).IsAssignableFrom(type))
        {
            var downAsyncMethod = type.GetMethod("DownAsync",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            return downAsyncMethod is not null;
        }

        return false;
    }

    private static bool IsMigrationBaseSubclass(Type type)
    {
        var baseType = type.BaseType;
        while (baseType is not null && baseType != typeof(object))
        {
            if (baseType.FullName == "FluentMigrator.Migration")
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }
}
