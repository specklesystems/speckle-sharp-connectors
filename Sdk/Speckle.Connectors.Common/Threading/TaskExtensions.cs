namespace Speckle.Connectors.Common.Threading;

public static class TaskExtensions
{
  public static ValueTask AsValueTask(this Task task) => new(task);

  public static ValueTask<T> AsValueTask<T>(this Task<T> task) => new(task);

  public static void Wait(this Task task) => task.GetAwaiter().GetResult();

  public static T Wait<T>(this Task<T> task) => task.GetAwaiter().GetResult();

  public static void Wait(this ValueTask task) => task.GetAwaiter().GetResult();

  public static T Wait<T>(this ValueTask<T> task) => task.GetAwaiter().GetResult();
#pragma warning disable CA1030
  public static async void FireAndForget(this ValueTask valueTask) => await valueTask;
#pragma warning restore CA1030
}
