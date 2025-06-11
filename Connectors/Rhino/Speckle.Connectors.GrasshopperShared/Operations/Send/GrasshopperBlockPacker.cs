using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

/// <summary>
/// Processes block instances (and underlying definitions) for publish.
/// </summary>
/// <remarks>
/// Only <see cref="SpeckleCollectionWrapper"/> can be published. Collections accept <see cref="SpeckleObjectWrapper"/>
/// and <see cref="SpeckleBlockInstanceWrapper"/>. They (for now) explicitly don't allow <see cref="SpeckleBlockDefinitionWrapper"/>.
/// Therefore, the case where a definition is directly provided won't occur.
/// </remarks>
internal sealed class GrasshopperBlockPacker
{
  /// <summary>
  /// Stores a map of instance definition id to instance definition proxy
  /// </summary>
  /// <remarks>
  /// Storing <see cref="InstanceDefinitionProxy"/> directly and not the wrapper (matching Rhino).
  /// </remarks>
  public Dictionary<string, InstanceDefinitionProxy> InstanceDefinitionProxies { get; } = [];

  /// <summary>
  /// Processes a <see cref="SpeckleBlockInstanceWrapper"/> by validating inputs before delegating to private method.
  /// Returns the <see cref="SpeckleObjectWrapper"/>s that make up the definition(s). These need to be added to the collection.
  /// </summary>
  public List<SpeckleObjectWrapper>? ProcessInstance(SpeckleBlockInstanceWrapper? blockInstance)
  {
    // NOTE: in pure gh workflows Definition might be null ALTHOUGH we're throwing an Exception 🤨. But rather safe than sorry.
    if (blockInstance?.Definition != null)
    {
      return ProcessDefinition(blockInstance.Definition);
    }

    return null;
  }

  /// <summary>
  /// Processes a <see cref="SpeckleBlockDefinitionWrapper"/> and adds it to the current collection (if not already present).
  /// </summary>
  private List<SpeckleObjectWrapper>? ProcessDefinition(SpeckleBlockDefinitionWrapper definition)
  {
    // Use wrapper's id as definitive identifier. Create if empty.
    definition.ApplicationId ??= Guid.NewGuid().ToString();
    string definitionId = definition.ApplicationId;

    // Only add if not in InstanceDefinitionProxies
    if (!InstanceDefinitionProxies.ContainsKey(definitionId))
    {
      // 💩 Sync proxy appId to wrapper appId. Can this mismatch even occur? Is this the right approach?
      InstanceDefinitionProxy proxy = definition.InstanceDefinitionProxy;
      proxy.applicationId = definitionId;
      InstanceDefinitionProxies[definitionId] = proxy;

      // Return objects to be added to collection (only happens once per definition)
      return definition.Objects;
    }

    // Already processed this definition - don't return objects
    return null;
  }
}
