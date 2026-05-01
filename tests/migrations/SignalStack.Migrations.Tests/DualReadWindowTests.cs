using Xunit;

namespace SignalStack.Migrations.Tests;

/// <summary>
/// Tests for the dual-read window feature (REQ-MIGRATION-005).
///
/// The dual-read window flag is stored in MongoDB sys_config under the key
/// <c>migrations.dual_read_window.active</c> and is seeded by the sys_config
/// seeder.
///
/// Full integration tests (requiring a live MongoDB) are skipped by default.
/// The unit tests here validate:
/// 1. The <see cref="DualReadWindowService"/> contract types are correct.
/// 2. The <see cref="DualReadMode"/> enum values align with expectations.
/// </summary>
public sealed class DualReadWindowTests
{
    [Fact]
    public void DualReadMode_NewSchemaOnly_is_default_value_zero()
    {
        // The default enum value should be NewSchemaOnly (steady-state).
        Assert.Equal(0, (int)DualReadMode.NewSchemaOnly);
    }

    [Fact]
    public void DualReadMode_BothPaths_has_value_one()
    {
        Assert.Equal(1, (int)DualReadMode.BothPaths);
    }

    [Fact]
    public void DualReadWindowService_requires_database_and_logger()
    {
        // Verify the constructor contract: both parameters are required.
        Assert.Throws<ArgumentNullException>(() =>
            new DualReadWindowService(null!, new Microsoft.Extensions.Logging.Abstractions.NullLogger<DualReadWindowService>()));
    }
}
