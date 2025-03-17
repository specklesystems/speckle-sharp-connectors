using Microsoft.Extensions.Logging;
using Speckle.Connectors.Logging;

namespace Speckle.Connectors.Common;

public sealed class SpeckleLogProvider(LoggerProvider speckleLogger) : ILoggerProvider
{
  public void Dispose() { }

  public ILogger CreateLogger(string categoryName) => new SpeckleLogger(speckleLogger.CreateLogger(categoryName));
}
