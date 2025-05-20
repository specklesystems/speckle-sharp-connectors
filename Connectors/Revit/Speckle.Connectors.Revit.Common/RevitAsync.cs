using Autodesk.Revit.UI;

namespace Speckle.Connectors.Revit.Common;

public static class RevitAsync
{
  public static Task<TResult> RunAsync<TResult>(Func<TResult> function) => global::Revit.Async.RevitTask.RunAsync(function);

  public static Task<TResult> RunAsync<TResult>(Func<Task<TResult>> function) => global::Revit.Async.RevitTask.RunAsync(function);

  public static Task RunAsync(Action action) => global::Revit.Async.RevitTask.RunAsync(action);

  public static Task RunAsync(Func<Task> handler) => global::Revit.Async.RevitTask.RunAsync(handler);

  public static void Initialize(UIControlledApplication application) => global::Revit.Async.RevitTask.Initialize(application);

  public static void Initialize(UIApplication application) => global::Revit.Async.RevitTask.Initialize(application);
}
