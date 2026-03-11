using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.DoubleNumerics;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;
using Transform = Speckle.Objects.Other.Transform;

namespace Speckle.Connectors.Revit.Operations.Receive;

public class RevitPreBakeSetupService
{
  private readonly ITransactionManager _transactionManager;
  private readonly RevitMaterialBaker _materialBaker;
  private readonly RevitViewBaker _viewBaker;
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;

  public RevitPreBakeSetupService(
    ITransactionManager transactionManager,
    RevitMaterialBaker materialBaker,
    RevitViewBaker viewBaker,
    RevitToHostCacheSingleton revitToHostCacheSingleton
  )
  {
    _transactionManager = transactionManager;
    _materialBaker = materialBaker;
    _viewBaker = viewBaker;
    _revitToHostCacheSingleton = revitToHostCacheSingleton;
  }

  public void ApplyIdModificationsAndBakeMaterials(
    UnpackStrategyResult unpackResult,
    RootObjectUnpackerResult unpackedRoot
  )
  {
    Dictionary<string, List<string>> originalToModifiedIds = new();

    foreach (LocalToGlobalMap localToGlobalMap in unpackResult.LocalToGlobalMaps)
    {
      if (
        localToGlobalMap.AtomicObject is ITransformable transformable
        && localToGlobalMap.Matrix.Count > 0
        && localToGlobalMap.AtomicObject["units"] is string units
      )
      {
        var id = localToGlobalMap.AtomicObject.id;
        var originalAppId = localToGlobalMap.AtomicObject.applicationId ?? id;

        ITransformable? newTransformable = null;
        foreach (var mat in localToGlobalMap.Matrix)
        {
          transformable.TransformTo(new Transform() { matrix = mat, units = units }, out newTransformable);
          transformable = newTransformable;
        }

        localToGlobalMap.AtomicObject = (newTransformable as Base)!;
        localToGlobalMap.AtomicObject.id = id;

        string modifiedAppId = $"{originalAppId}_{Guid.NewGuid().ToString("N")[..8]}";
        if (originalAppId != null)
        {
          if (!originalToModifiedIds.TryGetValue(originalAppId, out List<string>? modifiedIds))
          {
            modifiedIds = new List<string>();
            originalToModifiedIds[originalAppId] = modifiedIds;
          }
          modifiedIds.Add(modifiedAppId);
        }

        localToGlobalMap.AtomicObject.applicationId = modifiedAppId;
        localToGlobalMap.Matrix = new HashSet<Matrix4x4>();
      }
    }

    if (unpackedRoot.RenderMaterialProxies != null)
    {
      foreach (var proxy in unpackedRoot.RenderMaterialProxies)
      {
        var objectIdsToUse = new List<string>();
        foreach (var objectId in proxy.objects)
        {
          if (originalToModifiedIds.TryGetValue(objectId, out var modifiedIds))
          {
            objectIdsToUse.AddRange(modifiedIds);
          }
          else
          {
            objectIdsToUse.Add(objectId);
          }
        }
        proxy.objects = objectIdsToUse;
      }
    }

    UpdateAtomicObjectLookupWithModifiedIds(unpackResult.ParentDataObjectMap, originalToModifiedIds);

    if (unpackedRoot.RenderMaterialProxies != null)
    {
      _transactionManager.StartTransaction(true, "Baking materials");
      _materialBaker.MapLayersRenderMaterials(unpackedRoot);
      var map = _materialBaker.BakeMaterials(unpackedRoot.RenderMaterialProxies);
      foreach (var kvp in map)
      {
        _revitToHostCacheSingleton.MaterialsByObjectId.Add(kvp.Key, kvp.Value);
      }
      _transactionManager.CommitTransaction();
    }

    if (unpackedRoot.Cameras is not null)
    {
      _transactionManager.StartTransaction(true, "Baking views");
      _viewBaker.BakeViews(unpackedRoot.Cameras);
      _transactionManager.CommitTransaction();
    }
  }

  private void UpdateAtomicObjectLookupWithModifiedIds(
    Dictionary<string, DataObject> map,
    Dictionary<string, List<string>> originalToModifiedIds
  )
  {
    var entriesToAdd = new List<KeyValuePair<string, DataObject>>();
    var keysToRemove = new List<string>();

    foreach (var kvp in map)
    {
      if (originalToModifiedIds.TryGetValue(kvp.Key, out var modifiedIds))
      {
        keysToRemove.Add(kvp.Key);
        foreach (var modifiedId in modifiedIds)
        {
          entriesToAdd.Add(new KeyValuePair<string, DataObject>(modifiedId, kvp.Value));
        }
      }
    }

    foreach (var key in keysToRemove)
    {
      map.Remove(key);
    }

    foreach (var entry in entriesToAdd)
    {
      map[entry.Key] = entry.Value;
    }
  }
}
