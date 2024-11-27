using System.Runtime.CompilerServices;

namespace Speckle.Connectors.Common.Threading;

public static class Yield
{
  
  private static readonly Action s_yield = () => { };

  public static async ValueTask Force()
  {
    if (ThreadContext.IsMainThread)
    {
      await Task.Factory.StartNew(
        s_yield,
        CancellationToken.None,
        TaskCreationOptions.PreferFairness,
        SynchronizationContext.Current != null
          ? TaskScheduler.FromCurrentSynchronizationContext()
          : TaskScheduler.Current).ConfigureAwait(false);
    }
    else
    {
      await Task.Yield();
    }
  }

  public static ConfiguredValueTaskAwaitable BackToThread(this ValueTask valueTask) => valueTask.ConfigureAwait(true);
  public static ConfiguredValueTaskAwaitable BackToAny(this ValueTask valueTask) => valueTask.ConfigureAwait(false);
  public static ConfiguredTaskAwaitable BackToThread(this Task task) => task.ConfigureAwait(true);
  public static ConfiguredTaskAwaitable BackToAny(this Task task) => task.ConfigureAwait(false);
}
