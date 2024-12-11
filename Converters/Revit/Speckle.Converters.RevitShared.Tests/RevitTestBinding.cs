using System.Reflection;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Testing;
using Speckle.HostApps;
using Xunit.Abstractions;
using Xunit.Runners;

namespace Speckle.Converters.Revit2023.Tests;


public sealed class RevitTestBinding : IHostAppTestBinding
{
  private static readonly object s_consoleLock = new();

  private readonly List<ModelTest> _tests = new();
  private readonly List<ModelTestResult> _testResults = new();
  public string Name => "hostAppTestBiding";
  public IBrowserBridge Parent { get; }
  private readonly ITestExecutorFactory _testExecutorFactory;

  public RevitTestBinding(IBrowserBridge parent, ITestExecutorFactory testExecutorFactory)
  {
    _testExecutorFactory = testExecutorFactory;
    Parent = parent;
  }
  
  private string? LoadedModel => "empty";

  public string GetLoadedModel()
  {
    return LoadedModel ?? string.Empty;
  }

  public void ExecuteTest(string testName)
  {
    Console.WriteLine(testName);
  }

  public ModelTest[] GetTests()
  {
    if (string.IsNullOrEmpty(LoadedModel))
    {
      return [];
    }

    _tests.Clear();
    using var runner = _testExecutorFactory.Create(Assembly.GetExecutingAssembly());
    runner.OnDiscoveryMessage = OnDiscoveryMessage;
    runner.FindAll();

    return _tests.ToArray();
  }

  private void OnDiscoveryMessage(ITestCaseDiscoveryMessage info)
  {
    lock (_tests)
    {
      _tests.Add(new(info.TestCase.DisplayName, "NOT RUN"));
    }
  }

  public ModelTestResult[] GetTestsResults()
  {
    if (string.IsNullOrEmpty(LoadedModel))
    {
      return [];
    }

    _testResults.Clear();

    using var runner = _testExecutorFactory.Create(Assembly.GetExecutingAssembly());
    runner.OnExecutionComplete = OnExecutionComplete;
    runner.OnTestFailed = OnTestFailed;
    runner.OnTestPassed = OnTestPassed;
    runner.OnTestSkipped = OnTestSkipped;
    runner.RunAll();

    return _testResults.ToArray();
  }



  private void OnExecutionComplete(ExecutionCompleteInfo info)
  {
    lock (s_consoleLock)
    {
      Console.WriteLine(
        $"Finished: {info.TotalTests} tests in {Math.Round(info.ExecutionTime, 3)}s ({info.TestsFailed} failed, {info.TestsSkipped} skipped)");

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
