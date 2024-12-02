using System.Runtime.CompilerServices;

namespace Speckle.Connectors.Common.Threading;

public static class Yield
{
  public static ConfiguredValueTaskAwaitable BackToCurrent(this ValueTask valueTask) => valueTask.ConfigureAwait(true);

  public static ConfiguredValueTaskAwaitable BackToAny(this ValueTask valueTask) => valueTask.ConfigureAwait(false);

  public static ConfiguredTaskAwaitable BackToCurrent(this Task task) => task.ConfigureAwait(true);

  public static ConfiguredTaskAwaitable BackToAny(this Task task) => task.ConfigureAwait(false);

  public static ConfiguredTaskAwaitable<T> BackToCurrent<T>(this Task<T> task) => task.ConfigureAwait(true);

  public static ConfiguredTaskAwaitable<T> BackToAny<T>(this Task<T> task) => task.ConfigureAwait(false);
}
