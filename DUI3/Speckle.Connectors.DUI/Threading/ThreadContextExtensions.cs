namespace Speckle.Connectors.DUI.Threading;

public static class ThreadContextExtensions
{
  public static void RunOnMain(this IThreadContext threadContext, Action action) =>
    threadContext.RunOnThread(action, true);

  public static void RunOnWorker(this IThreadContext threadContext, Action action) =>
    threadContext.RunOnThread(action, false);

  public static Task RunOnMainAsync(this IThreadContext threadContext, Func<Task> action) =>
    threadContext.RunOnThreadAsync(action, true);

  public static Task RunOnWorkerAsync(this IThreadContext threadContext, Func<Task> action) =>
    threadContext.RunOnThreadAsync(action, false);

  public static Task<T> RunOnMainAsync<T>(this IThreadContext threadContext, Func<Task<T>> action) =>
    threadContext.RunOnThreadAsync(action, true);

  public static Task<T> RunOnWorkerAsync<T>(this IThreadContext threadContext, Func<Task<T>> action) =>
    threadContext.RunOnThreadAsync(action, false);
}
