namespace Speckle.Connectors.Common.Operations;

public interface ISyncToThread
{
  public Task<T> RunOnThread<T>(Func<T> func);
}
