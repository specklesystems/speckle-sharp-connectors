using Speckle.Objects.Data;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converters.Common.ToHost;

/// <summary>
/// Tracks DataObjects with InstanceProxy display values that need special handling during instance baking.
/// </summary>
public interface IDataObjectInstanceRegistry
{
  void Register(string dataObjectId, DataObject dataObject, List<InstanceProxy> instanceProxies);
  bool IsRegistered(string dataObjectId);
  IReadOnlyDictionary<string, DataObjectInstanceEntry> GetEntries();
  void Clear();

  /// <summary>
  /// Links a baked instance ID back to its parent DataObject.
  /// Called after instance baking to track which instances belong to which DataObject.
  /// </summary>
  void LinkInstanceToDataObject(string instanceProxyId, string bakedInstanceId);

  /// <summary>
  /// Gets all baked instance IDs for a given DataObject.
  /// </summary>
  List<string> GetInstanceIdsForDataObject(string dataObjectId);
}

/// <summary>
/// Represents a DataObject with InstanceProxy display values awaiting instance baking.
/// </summary>
public sealed record DataObjectInstanceEntry(DataObject DataObject, List<InstanceProxy> InstanceProxies);
