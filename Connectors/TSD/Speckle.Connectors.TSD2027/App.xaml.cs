using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.TSDShared;
using Speckle.Connectors.TSDShared.HostApp;

namespace Speckle.Connectors.TSD2027;

/// <summary>
/// Standalone WPF entry point for the Tekla Structural Designer connector. TSD launches this exe from its
/// Extensions folder, passing the gRPC port it is listening on via CLI arguments.
/// </summary>
internal sealed partial class App : Application
{
  private ServiceProvider? _container;
  private IDisposable? _loggingDisposable;

  protected override async void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    // DefaultThreadContext captures TaskScheduler.FromCurrentSynchronizationContext() at construction time.
    // WPF does not install a DispatcherSynchronizationContext until the dispatcher loop runs (after OnStartup),
    // so set one up front before the DI container - and therefore DefaultThreadContext - is built.
    if (SynchronizationContext.Current is not DispatcherSynchronizationContext)
    {
      SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));
    }

    var services = new ServiceCollection();
    _loggingDisposable = services.Initialize(HostApplications.TSD, HostAppVersion.v2027);
    services.AddTSD();

    _container = services.BuildServiceProvider();
    _container.UseDUI();

    // TSD launches us with the gRPC port it is listening on; bind to it before showing the UI so the bindings
    // can report the live model. When run without a port (no host) we still boot the DUI.
    if (TryGetPort(e.Args, out int port))
    {
      await _container.GetRequiredService<ITSDApplicationService>().ConnectAsync(port);
    }

    var webview = _container.GetRequiredService<DUI3ControlWebView>();
    var mainWindow = new MainWindow(webview);
    MainWindow = mainWindow;
    mainWindow.Show();
  }

  protected override void OnExit(ExitEventArgs e)
  {
    _container?.Dispose();
    _loggingDisposable?.Dispose();
    base.OnExit(e);
  }

  /// <summary>
  /// Reads the TSD gRPC port from the supported argument shapes: "--port 12345" and "--port=12345".
  /// </summary>
  private static bool TryGetPort(IReadOnlyList<string> args, out int port)
  {
    port = 0;

    for (int index = 0; index < args.Count; index++)
    {
      string argument = args[index];

      if (argument.Equals("--port", StringComparison.OrdinalIgnoreCase))
      {
        if (index + 1 >= args.Count)
        {
          return false;
        }

        return int.TryParse(args[index + 1], out port) && port > 0;
      }

      if (argument.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
      {
        string value = argument["--port=".Length..];
        return int.TryParse(value, out port) && port > 0;
      }
    }

    return false;
  }
}
