namespace Speckle.Connectors.Common.Threading;

public static class TaskExtensions
{
#pragma warning disable CA1030
  public static async void FireAndForget(this Task valueTask) => await valueTask;
#pragma warning restore CA1030

  /// <summary>
  /// Runs the task a worker thread and pumps the UI messages to keep responsiveness.
  /// </summary>
  /// <param name="task"></param>
  public static void WaitAndRunTask(this Task task)
  {
    while (!task.IsCompleted)
    {
      Application.DoEvents();
      Thread.Sleep(100);
    }

    if (task is { IsFaulted: true, Exception: not null })
    {
      throw task.Exception;
    }
  }
}
