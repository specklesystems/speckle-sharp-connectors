using TSD.API.Remoting;
using TSD.API.Remoting.Structure;
using TSD.API.Remoting.Units;

namespace Speckle.Connectors.TSDShared.HostApp;

internal interface ITSDApplicationService
{
  int? Port { get; }
  IApplication? Application { get; }
  string? ApplicationTitle { get; }
  string? ApplicationVersion { get; }
  Guid? ModelId { get; }
  bool IsConnected { get; }

  event EventHandler? SelectionChanged;

  Task ConnectAsync(int port);

  Task<IReadOnlyList<TSDSelectedEntity>> GetSelectedEntitiesAsync();

  Task<IReadOnlyList<IMember>> GetMembersForSendAsync(IReadOnlyList<string> objectIds);

  Task<IUnitBase?> GetLengthUnitAsync();

  Task<IReadOnlyList<double>> ConvertFromBaseAsync(IReadOnlyList<double> values, IUnitBase unit);
}
