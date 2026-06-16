using TSD.API.Remoting;
using TSD.API.Remoting.Common;
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

  IReadOnlyList<string> LoadingNames { get; }

  Task ConnectAsync(int port);

  Task RefreshLoadingsAsync();

  Task<IReadOnlyList<TSDSelectedEntity>> GetSelectedEntitiesAsync();

  Task<IReadOnlyList<IEntity>> GetObjectsForSendAsync(IReadOnlyList<string> objectIds);

  Task<IModel?> GetModelAsync();

  Task<IReadOnlyDictionary<Quantity, IUnitBase>> GetUnitsAsync(IEnumerable<Quantity> quantities);

  Task<IReadOnlyList<double>> ConvertFromBaseAsync(IReadOnlyList<double> values, IUnitBase unit);
}
