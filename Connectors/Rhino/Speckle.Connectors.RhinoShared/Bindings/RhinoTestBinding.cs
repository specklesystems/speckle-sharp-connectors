using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Testing;
using Speckle.Sdk.Common;
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
  private readonly ITestStorage _testStorage;
  private readonly IServiceProvider _serviceProvider;
  public string Name => "hostAppTestBiding";
  public IBrowserBridge Parent { get; }

  public RhinoTestBinding(IBrowserBridge parent, ITestStorageFactory testStorage, IServiceProvider serviceProvider)
  {
    Parent = parent;
    _testStorage = testStorage.CreateForUser();
    _serviceProvider = serviceProvider;
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
    
  
    _finished = new ManualResetEventSlim(false);
   using var runner = AssemblyRunner.WithoutAppDomain(Assembly.GetExecutingAssembly().Location);
    runner.OnDiscoveryComplete = OnDiscoveryComplete;
    runner.OnExecutionComplete = OnExecutionComplete;
    runner.OnTestFailed = OnTestFailed;
    runner.OnTestPassed = OnTestPassed;
    runner.OnTestSkipped = OnTestSkipped;
    runner.Start();
    _finished.Wait();
    
    _finished.Dispose();
    _finished = null;

    return [new("Receive"), new("Bar")];
  }
  public ModelTestResult[] GetTestsResults()
  {
    if (string.IsNullOrEmpty(LoadedModel))
    {
      return [];
    }

    return _testStorage.GetResults(LoadedModel.NotNull()).Select(x => new ModelTestResult(x.ModelName,
      x.TestName, x.Results, x.TimeStamp?.ToLocalTime().ToString() ?? "Unknown")).ToArray();
  }

  public void Receive()
  {
    var store = _serviceProvider.GetRequiredService<TestDocumentModelStore>();
    var card = new ReceiverModelCard()
    {
      ModelCardId = "test",
      AccountId = "test",
      ServerUrl = "",
      ProjectId = "",
      ProjectName = "",
      ModelId = "",
      ModelName = "",
      SelectedVersionId = ""
    };
    store.AddModel(card);
    var bridge = _serviceProvider.GetRequiredService<TestBrowserBridge>();
    var binding = ActivatorUtilities.CreateInstance<RhinoReceiveBinding>(_serviceProvider, store, bridge);
    binding.Receive("test").Wait();
    Console.WriteLine(string.Join(",", card.BakedObjectIds??[]));
  }
  
  private  void OnDiscoveryComplete(DiscoveryCompleteInfo info)
  {
    lock (s_consoleLock)
    {
      Console.WriteLine($"Running {info.TestCasesToRun} of {info.TestCasesDiscovered} tests...");
    }
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
      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine("[PASS] {0}", info.TestDisplayName);
      Console.ResetColor();
    }
  }

  private  void OnTestSkipped(TestSkippedInfo info)
  {
    lock (s_consoleLock)
    {
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine("[SKIP] {0}: {1}", info.TestDisplayName, info.SkipReason);
      Console.ResetColor();
    }
  }
}
