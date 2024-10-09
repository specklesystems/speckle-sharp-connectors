using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

public class PassthroughProgress(Action<ProgressArgs> progressCallback) : IProgress<ProgressArgs>
{
  public void Report(ProgressArgs value) => progressCallback.Invoke(value);
}
