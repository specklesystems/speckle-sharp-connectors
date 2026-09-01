using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.MicroStation.Operations.Send;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Services;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Shared-cell instancing, the AutoCAD-block pattern: a top-level (active-model)
/// <see cref="MgdElements.SharedCellElement"/> becomes an <see cref="InstanceProxy"/> whose
/// transform is the cell's basis transform; its <see cref="MgdElements.SharedCellDefinitionElement"/>
/// becomes an <see cref="InstanceDefinitionProxy"/> once, with the definition children registered as
/// atomic definition objects (converted later in the definition's LOCAL frame — the root object
/// builder wraps their conversion in <see cref="GeometryMapper.PushDefinitionFrame"/>). Shared cells
/// nested inside definitions recurse with increasing depth, exactly like nested blocks.
/// <para>
/// Shared cells living inside REFERENCE occurrences are not instanced — the display-value extractor
/// bakes those (documented parity deviation: repeated-geometry dedup is lost for that case only,
/// visual output is identical).
/// </para>
/// Expects to be a scoped dependency per send operation.
/// </summary>
public class MicroStationInstanceUnpacker(
  IInstanceObjectsManager<MicroStationRootObject, List<MicroStationRootObject>> instanceObjectsManager,
  GeometryMapper geometryMapper,
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  ILogger<MicroStationInstanceUnpacker> logger
) : IInstanceUnpacker<MicroStationRootObject>
{
  public UnpackResult<MicroStationRootObject> UnpackSelection(IEnumerable<MicroStationRootObject> objects)
  {
    foreach (MicroStationRootObject obj in objects)
    {
      // Only active-model shared cells are instanced; reference occurrences bake (see class docs).
      if (obj.OccurrenceTag.Length == 0 && obj.Element is MgdElements.SharedCellElement sharedCell)
      {
        UnpackInstance(sharedCell, obj.ApplicationId, 0);
      }
      instanceObjectsManager.AddAtomicObject(obj.ApplicationId, obj);
    }
    return instanceObjectsManager.GetUnpackResult();
  }

  private void UnpackInstance(MgdElements.SharedCellElement instance, string instanceId, int depth)
  {
    try
    {
      DPN.DgnFile? file = instance.DgnModel?.GetDgnFile();
      MgdElement? definition = file != null ? instance.GetDefinition(file) : null;
      if (definition == null)
      {
        logger.LogWarning("Shared cell '{Name}' has no definition; left un-instanced.", instance.CellName);
        return;
      }
      if (!instance.GetBasisTransform(out BG.DTransform3d placement))
      {
        logger.LogWarning("Shared cell '{Name}' basis transform unavailable; left un-instanced.", instance.CellName);
        return;
      }
      // dgnextract warns on non-uniform scale placements — the viewer assumes conformal instances.
      string definitionId = ((ulong)definition.ElementId).ToString();

      var instanceProxy = new InstanceProxy
      {
        applicationId = instanceId,
        definitionId = definitionId,
        maxDepth = depth,
        // A nested proxy's transform is definition-local — no global-origin shift for those.
        transform = geometryMapper.ToInstanceMatrix(placement, subtractGlobalOrigin: depth == 0),
        units = settingsStore.Current.SpeckleUnits,
      };
      instanceObjectsManager.AddInstanceProxy(instanceId, instanceProxy);

      if (
        !instanceObjectsManager.TryGetInstanceProxiesFromDefinitionId(
          definitionId,
          out List<InstanceProxy>? instanceProxiesWithSameDefinition
        )
      )
      {
        instanceProxiesWithSameDefinition = [];
        instanceObjectsManager.AddInstanceProxiesByDefinitionId(definitionId, instanceProxiesWithSameDefinition);
      }
      // Keep every proxy of this definition at the maximum depth seen (receive-order invariant).
      foreach (InstanceProxy proxy in instanceProxiesWithSameDefinition)
      {
        if (proxy.maxDepth < depth)
        {
          proxy.maxDepth = depth;
        }
      }
      instanceProxiesWithSameDefinition.Add(instanceObjectsManager.GetInstanceProxy(instanceId));

      // Definition already registered → only propagate the depth increase to its children.
      if (instanceObjectsManager.TryGetInstanceDefinitionProxy(definitionId, out InstanceDefinitionProxy? existing))
      {
        int depthDifference = depth - existing.maxDepth;
        if (depthDifference > 0)
        {
          instanceObjectsManager.UpdateChildrenMaxDepth(existing, depthDifference);
        }
        return;
      }

      var definitionProxy = new InstanceDefinitionProxy
      {
        applicationId = definitionId,
        objects = [],
        maxDepth = depth,
        name = string.IsNullOrEmpty(instance.CellName) ? definitionId : instance.CellName,
      };

      MgdElements.ChildElementCollection? children = definition.GetChildren();
      if (children != null)
      {
        foreach (MgdElement? child in children)
        {
          if (child == null || !child.IsGraphics)
          {
            continue;
          }
          string childId = ((ulong)child.ElementId).ToString();
          definitionProxy.objects.Add(childId);

          if (child is MgdElements.SharedCellElement nested)
          {
            UnpackInstance(nested, childId, depth + 1);
          }
          instanceObjectsManager.AddAtomicDefinitionObjectId(childId);
          instanceObjectsManager.AddAtomicObject(childId, MicroStationRootObject.InActiveModel(child));
        }
      }

      instanceObjectsManager.AddDefinitionProxy(definitionId, definitionProxy);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogError(ex, "Failed unpacking shared cell instance {Id}.", instanceId);
    }
  }
}
