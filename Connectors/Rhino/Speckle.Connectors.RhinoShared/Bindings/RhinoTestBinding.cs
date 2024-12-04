using System.Diagnostics;
using Rhino;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Testing;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Rhino.Bindings;

public interface IHostAppTestBinding : IBinding
{
  string GetLoadedModel();
  ModelTest[] GetTests();
  ModelTestResult[] GetTestsResults();
}

public class RhinoTestBinding : IHostAppTestBinding
{
  private readonly ITestStorage _testStorage;
  public string Name => "hostAppTestBiding";
  public IBrowserBridge Parent { get; }

  public RhinoTestBinding(IBrowserBridge parent, ITestStorage testStorage)
  {
    Parent = parent;
    _testStorage = testStorage;
  }

  private string? LoadedModel => RhinoDoc.ActiveDoc.Name;

  public string GetLoadedModel()
  {
    return LoadedModel ?? string.Empty;
  }

  public void ExecuteTest(string testName)
  {
    Debug.WriteLine(testName);
  }

  public ModelTest[] GetTests()
  {
    if (string.IsNullOrEmpty(LoadedModel))
    {
      return [];
    }

    return [new("Foo"), new("Bar")];
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
}
