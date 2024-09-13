using Microsoft.Extensions.Logging;
using Speckle.Connectors.Logging;

namespace Speckle.Connectors.Utils;

public sealed class SpeckleLogProvider(ISpeckleLogger speckleLogger) : ILoggerProvider
{
  public void Dispose() { }

  public ILogger CreateLogger(string categoryName) => new SpeckleLogger(speckleLogger);
}
