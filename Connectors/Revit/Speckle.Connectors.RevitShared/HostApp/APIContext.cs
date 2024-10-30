using Autodesk.Revit.UI;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// This class gives access to the Revit API context from anywhere in your codebase. This is essentially a
/// lite version of the Revit.Async package from Kennan Chan. Most of the functionality was taken from that code.
/// The main difference is that this class does not subscribe to the applicationIdling event from revit
/// which the docs say will impact the performance of Revit
/// </summary>
public sealed class APIContext : IDisposable
{
  private readonly SemaphoreSlim _semaphore = new(1, 1);
  private readonly UIControlledApplication _uiApplication;
  private readonly ExternalEventHandler<IExternalEventHandler, ExternalEvent> _factoryExternalEventHandler;
#pragma warning disable CA2213
  private readonly ExternalEvent _factoryExternalEvent;
#pragma warning restore CA2213

  public APIContext(UIControlledApplication application)
  {
    _uiApplication = application;
    _factoryExternalEventHandler = new(ExternalEvent.Create);
    _factoryExternalEvent = ExternalEvent.Create(_factoryExternalEventHandler);
  }

  public async Task<TResult> Run<TResult>(Func<UIControlledApplication, TResult> func)
  {
    await _semaphore.WaitAsync().ConfigureAwait(false);
    try
    {
      var handler = new ExternalEventHandler<UIControlledApplication, TResult>(func);
      using var externalEvent = await Run(_factoryExternalEventHandler, handler, _factoryExternalEvent)
        .ConfigureAwait(false);

      return await Run(handler, _uiApplication, externalEvent).ConfigureAwait(false);
    }
    finally
    {
      _semaphore.Release();
    }
  }

  public async Task Run(Action<UIControlledApplication> action) =>
    await Run<object>(app =>
      {
        action(app);
        return null!;
      })
      .ConfigureAwait(false);

  public async Task Run(Action action) =>
    await Run<object>(_ =>
      {
        action();
        return null!;
      })
      .ConfigureAwait(false);

  private async Task<TResult> Run<TParameter, TResult>(
    ExternalEventHandler<TParameter, TResult> handler,
    TParameter parameter,
    ExternalEvent externalEvent
  )
  {
    var task = handler.GetTask(parameter);
    externalEvent.Raise();

    return await task.ConfigureAwait(false);
  }

  public void Dispose()
  {
    _factoryExternalEvent.Dispose();
    _semaphore.Dispose();
  }
}

public enum HandlerStatus
{
  NotStarted,
  Started,
  IsCompleted,
  IsFaulted,
}

internal sealed class ExternalEventHandler<TParameter, TResult> : IExternalEventHandler
{
  private TaskCompletionSource<TResult> Result { get; set; }

  public Task<TResult> GetTask(TParameter parameter)
  {
    Parameter = parameter;
    Result = new TaskCompletionSource<TResult>();
    return Result.Task;
  }

  private readonly Func<TParameter, TResult> _func;

  public ExternalEventHandler(Func<TParameter, TResult> func)
  {
    this._func = func;
  }

  public HandlerStatus Status { get; private set; } = HandlerStatus.NotStarted;
  private TParameter Parameter { get; set; }

  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This is a very generic utility method for running things in a Revit context. If the result of the Run method is awaited, then the exception caught here will be raised there."
  )]
  public void Execute(UIApplication app)
  {
    Status = HandlerStatus.Started;
    try
    {
      var r = _func(Parameter);
      Result.SetResult(r);
      Status = HandlerStatus.IsCompleted;
    }
    catch (Exception ex)
    {
      Status = HandlerStatus.IsFaulted;
      Result.SetException(ex);
    }
  }

  public string GetName() => "SpeckleRevitContextEventHandler";
}
