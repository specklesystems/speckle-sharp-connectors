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

  Task ConnectAsync(int port);

  Task<IReadOnlyList<TSDSelectedEntity>> GetSelectedEntitiesAsync();

  Task<IReadOnlyList<IEntity>> GetObjectsForSendAsync(IReadOnlyList<string> objectIds);

  Task<IReadOnlyDictionary<int, ISlabData>> GetSlabDataAsync(IEnumerable<int> slabIndices);

  Task<IReadOnlyDictionary<Quantity, IUnitBase>> GetUnitsAsync(IEnumerable<Quantity> quantities);

  Task<IReadOnlyList<double>> ConvertFromBaseAsync(IReadOnlyList<double> values, IUnitBase unit);
}
