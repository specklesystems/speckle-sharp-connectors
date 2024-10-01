using Serilog.Extensions.Logging;

namespace Speckle.Connectors.Logging;

public sealed class LoggerProvider(SerilogLoggerProvider provider) : IDisposable
{
  public Logger CreateLogger(string categoryName) => new(provider.CreateLogger(categoryName));

  public void Dispose() => provider.Dispose();
}
