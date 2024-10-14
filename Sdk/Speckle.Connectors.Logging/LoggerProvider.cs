using Microsoft.Extensions.Logging;

namespace Speckle.Connectors.Logging;

public sealed class LoggerProvider : IDisposable
{
  private readonly ILoggerFactory _provider;

  internal LoggerProvider(ILoggerFactory provider) => _provider = provider;

  public Logger CreateLogger(string categoryName) => new(_provider.CreateLogger(categoryName));

  public void Dispose() => _provider.Dispose();
}
