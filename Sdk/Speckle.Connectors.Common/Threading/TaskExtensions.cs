namespace Speckle.Connectors.Common.Threading;

public static class TaskExtensions
{
#pragma warning disable CA1030
  public static async void FireAndForget(this Task valueTask) => await valueTask;
#pragma warning restore CA1030
}
