namespace Speckle.Connectors.Common.Threading;

public static class ThreadContextExtensions
{
  public static ValueTask RunOnMain(this IThreadContext threadContext, Action action) =>
    threadContext.RunOnThread(action, true);

  public static ValueTask RunOnWorker(this IThreadContext threadContext, Action action) =>
    threadContext.RunOnThread(action, false);

  public static ValueTask<T> RunOnMain<T>(this IThreadContext threadContext, Func<T> action) =>
    threadContext.RunOnThread(action, true);

  public static ValueTask<T> RunOnWorker<T>(this IThreadContext threadContext, Func<T> action) =>
    threadContext.RunOnThread(action, false);

  public static ValueTask RunOnMainAsync(this IThreadContext threadContext, Func<ValueTask> action) =>
    threadContext.RunOnThreadAsync(action, true);

  public static ValueTask RunOnWorkerAsync(this IThreadContext threadContext, Func<ValueTask> action) =>
    threadContext.RunOnThreadAsync(action, false);

  public static ValueTask<T> RunOnMainAsync<T>(this IThreadContext threadContext, Func<ValueTask<T>> action) =>
    threadContext.RunOnThreadAsync(action, true);

  public static ValueTask<T> RunOnWorkerAsync<T>(this IThreadContext threadContext, Func<ValueTask<T>> action) =>
    threadContext.RunOnThreadAsync(action, false);
}
