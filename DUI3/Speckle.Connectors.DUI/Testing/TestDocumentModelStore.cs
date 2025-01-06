using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.DUI.Testing;

public class TestDocumentModelStore(IJsonSerializer serializer) : DocumentModelStore(serializer)
{
  protected override void HostAppSaveState(string modelCardState) { }

  protected override void LoadState() { }
}

public class TestBrowserBridge : IBrowserBridge
{
  public string FrontendBoundName => "TestBrowserBridge";

  public void AssociateWithBinding(IBinding binding) { }

  public string[] GetBindingsMethodNames() => throw new NotImplementedException();

  public void RunMethod(string methodName, string requestId, string args) =>
    Console.WriteLine($"RunMethod: {methodName}");

  public Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> action) => throw new NotImplementedException();

  public Task RunOnMainThreadAsync(Func<Task> action) => throw new NotImplementedException();

  public Task Send(string eventName, CancellationToken cancellationToken = default)
  {
    Console.WriteLine($"RunMethod: {eventName}");
    return Task.CompletedTask;
  }

  public Task Send<T>(string eventName, T data, CancellationToken cancellationToken = default)
    where T : class
  {
    Console.WriteLine($"RunMethod: {eventName}");
    return Task.CompletedTask;
  }

  public void Send2<T>(string eventName, T data) where T : class   
  {
    Console.WriteLine($"RunMethod: {eventName}");
  }

#pragma warning disable CA1065
  public ITopLevelExceptionHandler TopLevelExceptionHandler => throw new NotImplementedException();
#pragma warning restore CA1065
}
