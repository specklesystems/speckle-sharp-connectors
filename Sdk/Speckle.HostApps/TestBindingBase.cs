using System.Reflection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Testing;
using Xunit.Abstractions;
using Xunit.Runners;

namespace Speckle.HostApps;

public abstract class TestBindingBase(ITestExecutorFactory testExecutorFactory) : IHostAppTestBinding
{
  public string Name => "hostAppTestBiding";
  private static readonly object s_consoleLock = new();
  private readonly List<ModelTest> _tests = new();
  private readonly List<ModelTestResult> _testResults = new();

  public abstract IEnumerable<Assembly> GetAssemblies();

  public abstract IBrowserBridge Parent { get; }

  public string GetLoadedModel() => string.Empty;

  public ModelTest[] GetTests() => GetTests(GetAssemblies());

  public ModelTestResult[] GetTestsResults() => GetTestsResults(GetAssemblies());

  public ModelTest[] GetTests(IEnumerable<Assembly> assemblies)
  {
    _tests.Clear();
    var executors = new List<TestExecutor>();
    foreach (var assembly in assemblies ?? [])
    {
      var runner = testExecutorFactory.Create(assembly);
      runner.OnDiscoveryMessage = OnDiscoveryMessage;
      runner.StartFind();
      executors.Add(runner);
    }

    foreach (var executor in executors)
    {
      executor.WaitForExecutionFinish();
      executor.Dispose();
    }

    return _tests.ToArray();
  }

  private void OnDiscoveryMessage(ITestCaseDiscoveryMessage info)
  {
    lock (_tests)
    {
      _tests.Add(new(info.TestCase.DisplayName, "NOT RUN"));
    }
  }

  public ModelTestResult[] GetTestsResults(IEnumerable<Assembly> assemblies)
  {
    _testResults.Clear();
    var executors = new List<TestExecutor>();
    foreach (var assembly in assemblies ?? [])
    {
      var runner = testExecutorFactory.Create(assembly);
      runner.OnExecutionComplete = OnExecutionComplete;
      runner.OnTestFailed = OnTestFailed;
      runner.OnTestPassed = OnTestPassed;
      runner.OnTestSkipped = OnTestSkipped;
      runner.StartExecution();
      executors.Add(runner);
    }

    foreach (var executor in executors)
    {
      executor.WaitForExecutionFinish();
      executor.Dispose();
    }
    return _testResults.ToArray();
  }

  private void OnExecutionComplete(ExecutionCompleteInfo info)
  {
    lock (s_consoleLock)
    {
      Console.WriteLine(
        $"Finished: {info.TotalTests} tests in {Math.Round(info.ExecutionTime, 3)}s ({info.TestsFailed} failed, {info.TestsSkipped} skipped)"
      );
    }
  }

  private void OnTestFailed(TestFailedInfo info)
  {
    lock (s_consoleLock)
    {
      _testResults.Add(new ModelTestResult(info.TestDisplayName, "FAIL", DateTime.UtcNow.ToString()));
      Console.ForegroundColor = ConsoleColor.Red;

      Console.WriteLine("[FAIL] {0}: {1}", info.TestDisplayName, info.ExceptionMessage);
      if (info.ExceptionStackTrace != null)
      {
        Console.WriteLine(info.ExceptionStackTrace);
      }

      Console.ResetColor();
    }
  }

  private void OnTestPassed(TestPassedInfo info)
  {
    lock (s_consoleLock)
    {
      _testResults.Add(new ModelTestResult(info.TestDisplayName, "PASS", DateTime.UtcNow.ToString()));
      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine("[PASS] {0}", info.TestDisplayName);
      Console.ResetColor();
    }
  }

  private void OnTestSkipped(TestSkippedInfo info)
  {
    lock (s_consoleLock)
    {
      _testResults.Add(new ModelTestResult(info.TestDisplayName, "SKIPPED", DateTime.UtcNow.ToString()));
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine("[SKIP] {0}: {1}", info.TestDisplayName, info.SkipReason);
      Console.ResetColor();
    }
  }
}
