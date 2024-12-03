using System.Runtime.CompilerServices;

namespace Speckle.Connectors.Common.Threading;

public static class TaskExtensions
{
  public static ConfiguredValueTaskAwaitable<T> BackToCurrent<T>(this ValueTask<T> valueTask) =>
    valueTask.ConfigureAwait(true);

  public static ConfiguredValueTaskAwaitable<T> BackToAny<T>(this ValueTask<T> valueTask) =>
    valueTask.ConfigureAwait(false);

  public static ConfiguredValueTaskAwaitable BackToCurrent(this ValueTask valueTask) => valueTask.ConfigureAwait(true);

  public static ConfiguredValueTaskAwaitable BackToAny(this ValueTask valueTask) => valueTask.ConfigureAwait(false);

  public static ConfiguredTaskAwaitable BackToCurrent(this Task task) => task.ConfigureAwait(true);

  public static ConfiguredTaskAwaitable BackToAny(this Task task) => task.ConfigureAwait(false);

  public static ConfiguredTaskAwaitable<T> BackToCurrent<T>(this Task<T> task) => task.ConfigureAwait(true);

  public static ConfiguredTaskAwaitable<T> BackToAny<T>(this Task<T> task) => task.ConfigureAwait(false);

  public static ValueTask AsValueTask(this Task task) => new(task);

  public static ValueTask<T> AsValueTask<T>(this Task<T> task) => new(task);

  public static void Wait(this Task task) => task.GetAwaiter().GetResult();

  public static T Wait<T>(this Task<T> task) => task.GetAwaiter().GetResult();

  public static void Wait(this ValueTask task) => task.GetAwaiter().GetResult();

  public static T Wait<T>(this ValueTask<T> task) => task.GetAwaiter().GetResult();
}
