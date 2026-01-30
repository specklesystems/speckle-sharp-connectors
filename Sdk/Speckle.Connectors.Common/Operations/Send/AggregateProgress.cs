namespace Speckle.Connectors.Common.Operations.Send;

public sealed class AggregateProgress<T> : IProgress<T>
{
  private readonly IProgress<T>[] _progresses;

  public AggregateProgress(params IProgress<T>[] progresses)
  {
    _progresses = progresses;
  }

  public void Report(T value)
  {
    foreach (var progress in _progresses)
    {
      progress.Report(value);
    }
  }
}
