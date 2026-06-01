using TSD.API.Remoting;

namespace Speckle.Connectors.TSDShared.HostApp;

/// <summary>
/// Holds the connection to the TSD instance that launched this connector. TSD starts our exe as a child process and
/// passes the gRPC port it is listening on; <see cref="ConnectAsync"/> binds to that port via the Remoting API.
/// </summary>
internal interface ITSDApplicationService
{
  int? Port { get; }
  IApplication? Application { get; }
  string? ApplicationTitle { get; }
  string? ApplicationVersion { get; }
  bool IsConnected { get; }

  Task ConnectAsync(int port);
}
