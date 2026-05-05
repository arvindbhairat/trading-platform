using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests that <see cref="RmeModuleExtensions.AddRmeModule"/> registers all
/// required RME services correctly.
/// REQ-RME-001, REQ-RME-012.
/// </summary>
public sealed class RmeModuleExtensionsTests
{
    private static IServiceProvider BuildServiceProvider(Action<IConfigurationBuilder>? configure = null)
    {
        var configBuilder = new ConfigurationBuilder();
        configure?.Invoke(configBuilder);
        var configuration = configBuilder.Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Register IConfiguration so that options binding works
        services.AddSingleton<IConfiguration>(configuration);

        // Register a mock IMongoDatabase for services that depend on it
        var mockDb = new Mock<IMongoDatabase>();
        services.AddSingleton<IMongoDatabase>(mockDb.Object);

        services.AddRmeModule(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddRmeModule_registers_TransitionValidator_as_singleton()
    {
        var provider = BuildServiceProvider();

        var instance1 = provider.GetRequiredService<ITransitionValidator>();
        var instance2 = provider.GetRequiredService<ITransitionValidator>();

        Assert.NotNull(instance1);
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void AddRmeModule_registers_EquityBaseReader_as_singleton()
    {
        var provider = BuildServiceProvider();

        var instance1 = provider.GetRequiredService<IEquityBaseReader>();
        var instance2 = provider.GetRequiredService<IEquityBaseReader>();

        Assert.NotNull(instance1);
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void AddRmeModule_registers_FixedPercentageSizingModel()
    {
        var provider = BuildServiceProvider();

        var models = provider.GetRequiredService<IEnumerable<ISizingModel>>();

        Assert.Contains(models, m => m is FixedPercentageSizingModel);
    }

    [Fact]
    public void AddRmeModule_registers_all_sizing_models()
    {
        var provider = BuildServiceProvider();

        var models = provider.GetRequiredService<IEnumerable<ISizingModel>>();

        Assert.Contains(models, m => m is FixedPercentageSizingModel);
        Assert.Contains(models, m => m is AtrSizingModel);
        Assert.Contains(models, m => m is HeatBasedSizingModel);
        Assert.Contains(models, m => m is DrawdownAdjustedSizingModel);
    }

    [Fact]
    public void AddRmeModule_registers_all_stop_loss_types()
    {
        var provider = BuildServiceProvider();

        var stops = provider.GetRequiredService<IEnumerable<IStopLoss>>();

        Assert.Contains(stops, s => s is FixedStopLoss);
        Assert.Contains(stops, s => s is AtrStopLoss);
        Assert.Contains(stops, s => s is TrailingStopLoss);
        Assert.Contains(stops, s => s is AtrTrailingStopLoss);
        Assert.Contains(stops, s => s is SwingLowStopLoss);
        Assert.Contains(stops, s => s is BreakevenStopLoss);
    }

    [Fact]
    public void AddRmeModule_registers_RmeBackgroundService_as_hosted_service()
    {
        var provider = BuildServiceProvider();

        var hostedServices = provider.GetRequiredService<IEnumerable<IHostedService>>();

        Assert.Contains(hostedServices, s => s is RmeBackgroundService);
    }

    [Fact]
    public void AddRmeModule_registers_RmeAdvisoryService()
    {
        var provider = BuildServiceProvider();

        var advisory = provider.GetRequiredService<IRmeAdvisoryService>();

        Assert.IsType<RmeAdvisoryService>(advisory);
    }

    [Fact]
    public void AddRmeModule_registers_PortfolioHeatCalculator()
    {
        var provider = BuildServiceProvider();

        var calc = provider.GetRequiredService<IPortfolioHeatCalculator>();

        Assert.NotNull(calc);
    }

    [Fact]
    public void AddRmeModule_registers_DrawdownTracker()
    {
        var provider = BuildServiceProvider();

        var tracker = provider.GetRequiredService<IDrawdownTracker>();

        Assert.NotNull(tracker);
    }

    [Fact]
    public void AddRmeModule_registers_PyramidingAdvisoryService()
    {
        var provider = BuildServiceProvider();

        var advisory = provider.GetRequiredService<IPyramidingAdvisoryService>();

        Assert.NotNull(advisory);
    }

    [Fact]
    public void AddRmeModule_registers_ZScoreAdvisoryService()
    {
        var provider = BuildServiceProvider();

        var advisory = provider.GetRequiredService<IZScoreAdvisoryService>();

        Assert.NotNull(advisory);
    }

    [Fact]
    public void AddRmeModule_registers_TrailingStopSyncService()
    {
        var provider = BuildServiceProvider();

        var service = provider.GetRequiredService<ITrailingStopSyncService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void AddRmeModule_registers_RmeModuleOptions_with_validation()
    {
        var provider = BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RmeModuleOptions>>();

        Assert.NotNull(options.Value);
        Assert.Equal(200, options.Value.MaxActivePositions);
    }

    [Fact]
    public void AddRmeModule_binds_RmeModuleOptions_from_configuration()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Worker:Rme:Module:MaxActivePositions"] = "50",
        };

        var provider = BuildServiceProvider(cfg =>
        {
            cfg.AddInMemoryCollection(configValues);
        });

        var options = provider.GetRequiredService<IOptions<RmeModuleOptions>>();

        Assert.Equal(50, options.Value.MaxActivePositions);
    }
}
