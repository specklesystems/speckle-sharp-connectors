using Speckle.Objects.Data;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converters.Common.ToHost;

/// <summary>
/// Tracks DataObjects with InstanceProxy display values that need special handling during on load.
/// </summary>
/// <remarks>
/// In Rhino-land: converter registers these instead of returning geometry, and the instance baker uses this to create
/// grouped block instances with proper metadata applied.
/// </remarks>
public sealed class DataObjectInstanceRegistry : IDataObjectInstanceRegistry
{
  private readonly Dictionary<string, DataObjectInstanceEntry> _entries = new();
  private readonly Dictionary<string, string> _instanceProxyToDataObject = new();
  private readonly Dictionary<string, List<string>> _dataObjectToBakedInstances = new();

  public void Register(string dataObjectId, DataObject dataObject, List<InstanceProxy> instanceProxies)
  {
    _entries[dataObjectId] = new DataObjectInstanceEntry(dataObject, instanceProxies);

    // track reverse mapping for each proxy
    foreach (var proxy in instanceProxies)
    {
      var proxyId = proxy.applicationId ?? proxy.id.NotNull();
      _instanceProxyToDataObject[proxyId] = dataObjectId;
    }

    _dataObjectToBakedInstances[dataObjectId] = new List<string>();
  }

  public bool IsRegistered(string dataObjectId) => _entries.ContainsKey(dataObjectId);

  public IReadOnlyDictionary<string, DataObjectInstanceEntry> GetEntries() => _entries;

  public void LinkInstanceToDataObject(string instanceProxyId, string bakedInstanceId)
  {
    if (_instanceProxyToDataObject.TryGetValue(instanceProxyId, out var dataObjectId))
    {
      _dataObjectToBakedInstances[dataObjectId].Add(bakedInstanceId);
    }
  }

  public List<string> GetInstanceIdsForDataObject(string dataObjectId) =>
    _dataObjectToBakedInstances.TryGetValue(dataObjectId, out var ids) ? ids : new List<string>();

  public void Clear()
  {
    _entries.Clear();
    _instanceProxyToDataObject.Clear();
    _dataObjectToBakedInstances.Clear();
  }
}
