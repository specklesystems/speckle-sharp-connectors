using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Testing;

public class TestBrowserBridge : IBrowserBridge
{
  public static TestBrowserBridge Instance { get; } = new();
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
  public ITopLevelExceptionHandler TopLevelExceptionHandler => new TestTopLevelExceptionHandler(this);
#pragma warning restore CA1065
}

public class TestTopLevelExceptionHandler(TestBrowserBridge parent) : ITopLevelExceptionHandler
{
  public IBrowserBridge Parent => parent;
  public string Name => "testTopLevelExceptionHandler";
  public void CatchUnhandled(Action function) => function();

  public Result<T> CatchUnhandled<T>(Func<T> function) => new Result<T>(function());

  public async Task<Result> CatchUnhandledAsync(Func<Task> function)
  {
     await function();
     return new Result();
  }

  public async Task<Result<T>> CatchUnhandledAsync<T>(Func<Task<T>> function) 
  {
    return new (await function());
  }

  public async void FireAndForget(Func<Task> function) => await function();
}
