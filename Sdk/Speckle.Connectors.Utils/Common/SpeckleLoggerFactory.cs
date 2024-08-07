using Microsoft.Extensions.Logging;
using Speckle.Logging;

namespace Speckle.Connectors.Utils.Common;

public sealed class SpeckleLoggerFactory : ILoggerFactory
{
  public void Dispose() { }

  public ILogger CreateLogger(string categoryName) => new SpeckleLogger(SpeckleLog.Create(categoryName));

  public void AddProvider(ILoggerProvider provider) => throw new NotImplementedException();
}
