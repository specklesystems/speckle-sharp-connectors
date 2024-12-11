using System.Reflection;
using Rhino;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Testing;
using Speckle.HostApps;
using Xunit.Abstractions;
using Xunit.Runners;

namespace Speckle.Connectors.Rhino.Bindings;

public interface IHostAppTestBinding : IBinding
{
  string GetLoadedModel();
  ModelTest[] GetTests();
  ModelTestResult[] GetTestsResults();
}

public sealed class RhinoTestBinding : IHostAppTestBinding, IDisposable
{   
  private static readonly object s_consoleLock = new();  
  private ManualResetEventSlim? _finished;
  
  private readonly List<ModelTest> _tests = new();
  private readonly List<ModelTestResult> _testResults = new();
  public string Name => "hostAppTestBiding";
  public IBrowserBridge Parent { get; }
  private readonly ITestExecutorFactory _testExecutorFactory;

  public RhinoTestBinding(IBrowserBridge parent, ITestExecutorFactory testExecutorFactory)
  {
    _testExecutorFactory = testExecutorFactory;
    Parent = parent;
  }
  public void Dispose() => _finished?.Dispose();


  private string? LoadedModel => RhinoDoc.ActiveDoc.Name;

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
  private  void OnDiscoveryMessage(ITestCaseDiscoveryMessage info)
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
    
    
     
    _finished = new ManualResetEventSlim(false);
    using var runner = AssemblyRunner.WithoutAppDomain(Assembly.GetExecutingAssembly().Location);
    runner.OnExecutionComplete = OnExecutionComplete;
    runner.OnTestFailed = OnTestFailed;
    runner.OnTestPassed = OnTestPassed;
    runner.OnTestSkipped = OnTestSkipped;
    runner.Start();
    _finished.Wait();
    
    _finished.Dispose();
    _finished = null;

    return _testResults.ToArray();
  }
  
 

  private void OnExecutionComplete(ExecutionCompleteInfo info)
  {
    lock (s_consoleLock)
    {
      Console.WriteLine(
        $"Finished: {info.TotalTests} tests in {Math.Round(info.ExecutionTime, 3)}s ({info.TestsFailed} failed, {info.TestsSkipped} skipped)");
    
  }

    _finished?.Set();
  }

  private  void OnTestFailed(TestFailedInfo info)
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

  private  void OnTestPassed(TestPassedInfo info)
  {
    lock (s_consoleLock)
    {
      _testResults.Add(new ModelTestResult(info.TestDisplayName, "PASS", DateTime.UtcNow.ToString()));
      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine("[PASS] {0}", info.TestDisplayName);
      Console.ResetColor();
    }
  }

  private  void OnTestSkipped(TestSkippedInfo info)
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
