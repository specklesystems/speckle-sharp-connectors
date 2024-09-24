using Microsoft.Extensions.Logging;
using Speckle.Connectors.Logging;

namespace Speckle.Connectors.Common;

public sealed class SpeckleLogProvider(Logger speckleLogger) : ILoggerProvider
{
  public void Dispose() { }

  public ILogger CreateLogger(string categoryName) => new SpeckleLogger(speckleLogger);
}
