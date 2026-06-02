using Xunit;

namespace SignalStack.Worker.Tests;

public sealed class WorkerSingletonInfrastructureLintTests
{
  [Fact]
  public void Worker_app_service_bicep_pins_workerCount_to_one_and_declares_no_autoscale_resource()
  {
    var repoRoot = GetRepoRoot();
    var bicepPath = Path.Combine(repoRoot, "infra", "azure", "worker-appservice.bicep");
    var bicep = File.ReadAllText(bicepPath);

    Assert.Contains("var workerCount = 1", bicep);
    Assert.DoesNotContain("Microsoft.Insights/autoscalesettings", bicep, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("capacity: workerCount", bicep);
  }

  [Fact]
  public void Singleton_preflight_script_exists_and_includes_autoscale_check()
  {
    var repoRoot = GetRepoRoot();
    var scriptPath = Path.Combine(repoRoot, "infra", "azure", "scripts", "Test-WorkerSingletonPreflight.ps1");
    var script = File.ReadAllText(scriptPath);

    Assert.Contains("az monitor autoscale list", script);
  }

  private static string GetRepoRoot()
  {
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
  }
}
