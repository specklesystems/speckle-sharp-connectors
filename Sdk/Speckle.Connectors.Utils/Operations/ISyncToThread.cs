namespace Speckle.Connectors.Utils.Operations;

public interface ISyncToThread
{
  public Task<T> RunOnThread<T>(Func<Task<T>> func);
}
