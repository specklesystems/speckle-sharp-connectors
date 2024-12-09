using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
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
  private readonly IServiceProvider _serviceProvider;
  public string Name => "hostAppTestBiding";
  public IBrowserBridge Parent { get; }

  public RhinoTestBinding(IBrowserBridge parent, ITestStorageFactory testStorage, IServiceProvider serviceProvider)
  {
    Parent = parent;
    _testStorage = testStorage.CreateForUser();
    _serviceProvider = serviceProvider;
  }

  private string? LoadedModel => RhinoDoc.ActiveDoc.Name;

  public string GetLoadedModel()
  {
    return LoadedModel ?? string.Empty;
  }

  public async Task ExecuteTest(string testName)
  {
    Console.WriteLine(testName);
    var method = typeof(RhinoTestBinding).GetMethods().FirstOrDefault(x => x.Name == testName);
    if (method is null)
    {
      return;
    }
    object? resultTyped = method.Invoke(this, []);

    // Was the method called async?
    if (resultTyped is not Task resultTypedTask)
    {
      // Regular method: no need to await things
      return;
    }

    // It's an async call
    await resultTypedTask.ConfigureAwait(false);

    // If has a "Result" property return the value otherwise null (Task<void> etc)
    PropertyInfo? resultProperty = resultTypedTask.GetType().GetProperty(nameof(Task<object>.Result));
    object? taskResult = resultProperty?.GetValue(resultTypedTask);
  }

  public ModelTest[] GetTests()
  {
    if (string.IsNullOrEmpty(LoadedModel))
    {
      return [];
    }

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
}
