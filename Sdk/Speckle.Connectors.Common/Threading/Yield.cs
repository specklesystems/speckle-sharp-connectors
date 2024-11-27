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
}
