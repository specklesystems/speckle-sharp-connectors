using Microsoft.Extensions.Logging;
using Speckle.Sdk;
using TSD.API.Remoting;

namespace Speckle.Connectors.TSDShared.HostApp;

internal sealed class TSDApplicationService : ITSDApplicationService, IDisposable
{
  private readonly ILogger<TSDApplicationService> _logger;
  private bool _disposed;

  public TSDApplicationService(ILogger<TSDApplicationService> logger)
  {
    _logger = logger;
  }

  public int? Port { get; private set; }
  public IApplication? Application { get; private set; }
  public string? ApplicationTitle { get; private set; }
  public string? ApplicationVersion { get; private set; }
  public bool IsConnected => Application is not null;

  public async Task ConnectAsync(int port)
  {
    Port = port;

    try
    {
      Application = await ApplicationFactory.ConnectToRunningApplicationAsync(port).ConfigureAwait(false);

      if (Application is null)
      {
        _logger.LogWarning("Could not connect to TSD on port {Port}", port);
        return;
      }

      ApplicationTitle = await Application.GetApplicationTitleAsync().ConfigureAwait(false);
      ApplicationVersion = await Application.GetVersionStringAsync().ConfigureAwait(false);

      _logger.LogInformation(
        "Connected to TSD: {Title} ({Version}) on port {Port}",
        ApplicationTitle,
        ApplicationVersion,
        port
      );
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to connect to TSD on port {Port}", port);
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    if (Application is IAsyncDisposable asyncDisposable)
    {
      asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    else
    {
      Application?.Dispose();
    }
  }
}
