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
  public void Worker_release_workflow_runs_the_singleton_preflight_script()
  {
    var repoRoot = GetRepoRoot();
    var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "worker-release-preflight.yml");
    var workflow = File.ReadAllText(workflowPath);

    Assert.Contains("infra/azure/scripts/Test-WorkerSingletonPreflight.ps1", workflow);
    Assert.Contains("az monitor autoscale list", File.ReadAllText(Path.Combine(repoRoot, "infra", "azure", "scripts", "Test-WorkerSingletonPreflight.ps1")));
  }

  private static string GetRepoRoot()
  {
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
  }
}
