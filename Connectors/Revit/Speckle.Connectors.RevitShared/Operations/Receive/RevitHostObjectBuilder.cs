using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Transform = Speckle.Objects.Other.Transform;

namespace Speckle.Connectors.Revit.Operations.Receive;

public sealed class RevitHostObjectBuilder(
  IRootToHostConverter converter,
  IConverterSettingsStore<RevitConversionSettings> converterSettings,
  ITransactionManager transactionManager,
  ISdkActivityFactory activityFactory,
  ILocalToGlobalUnpacker localToGlobalUnpacker,
  RevitGroupBaker groupManager,
  RevitMaterialBaker materialBaker,
  RootObjectUnpacker rootObjectUnpacker,
  ILogger<RevitHostObjectBuilder> logger,
  IThreadContext threadContext,
  RevitToHostCacheSingleton revitToHostCacheSingleton,
  ITypedConverter<
    (Base atomicObject, IReadOnlyCollection<Matrix4x4> matrix),
    DirectShape
  > localToGlobalDirectShapeConverter,
  IReceiveConversionHandler conversionHandler
) : IHostObjectBuilder, IDisposable
{
  /*public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  ) =>
    threadContext.RunOnMainAsync(
      () => Task.FromResult(BuildSync(rootObject, projectName, modelName, onOperationProgressed, cancellationToken))
    );*/

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    bool x = false;
#if REVIT2026
    x = converterSettings.Current.Document.IsInEditMode();
#endif

    /*bool mod = converterSettings.Current.Document.IsModified;
    bool ro = converterSettings.Current.Document.IsReadOnly;
    bool mof = converterSettings.Current.Document.IsModifiable;*/
    if (x)
    {
      throw new ConversionException($"We are in modify mode!!!!!");
    }

    return threadContext.RunOnMainAsync(
      () => Task.FromResult(BuildSync(rootObject, projectName, modelName, onOperationProgressed, cancellationToken))
    );
  }

  private HostObjectBuilderResult BuildSync(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // TODO: formalise getting transform info from rootObject. this dict access is gross.
    Autodesk.Revit.DB.Transform? referencePointTransformFromRootObject = null;
    if (
      rootObject.DynamicPropertyKeys.Contains(ReferencePointHelper.REFERENCE_POINT_TRANSFORM_KEY)
      && rootObject[ReferencePointHelper.REFERENCE_POINT_TRANSFORM_KEY] is Dictionary<string, object> transformDict
      && transformDict.TryGetValue("transform", out var transformValue)
    )
    {
      referencePointTransformFromRootObject = ReferencePointHelper.GetTransformFromRootObject(transformValue);
    }

    var baseGroupName = $"Project {projectName}: Model {modelName}"; // TODO: unify this across connectors!

    onOperationProgressed.Report(new("Converting", null));
    using var activity = activityFactory.Start("Build");

    // 0 - Clean then Rock n Roll! 🎸
    {
      activityFactory.Start("Pre receive clean");
      transactionManager.StartTransaction(true, "Pre receive clean");
      try
      {
        PreReceiveDeepClean(baseGroupName);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to clean up before receive in Revit");
      }

      transactionManager.CommitTransaction();
    }

    // 1 - Unpack objects and proxies from root commit object
    var unpackedRoot = rootObjectUnpacker.Unpack(rootObject);
    var localToGlobalMaps = localToGlobalUnpacker.Unpack(
      unpackedRoot.DefinitionProxies,
      unpackedRoot.ObjectsToConvert.ToList()
    );

    // NOTE: below is 💩... https://github.com/specklesystems/speckle-sharp-connectors/pull/813 broke sketchup to revit workflow
    // ids were modified to fix receiving instances [CNX-1707](https://linear.app/speckle/issue/CNX-1707/revit-curves-and-meshes-in-blocks-come-as-duplicated)
    // but we then broke sketchup to revit because applicationIds in proxies didn't match modified application ids which cam from #813 hack
    // given urgency to get sketchup to revit workflow back up and running, temp fix involves setting modified ids before material baking, mapping original app ids to modified ids and using those
    // this way, CNX-1707 fix stays in tact and we fix sketchup to revit
    // TODO: TransformTo and material baking needs to be fixed in Revit!!

    // create a mapping from original to modified IDs <- so that we can actually map ids in the proxies to the objects
    Dictionary<string, string> originalToModifiedIds = new();

    // modify application IDs BEFORE material baking
    foreach (LocalToGlobalMap localToGlobalMap in localToGlobalMaps)
    {
      if (
        localToGlobalMap.AtomicObject is ITransformable transformable
        && localToGlobalMap.Matrix.Count > 0
        && localToGlobalMap.AtomicObject["units"] is string units
      )
      {
        var id = localToGlobalMap.AtomicObject.id;
        //var originalAppId = localToGlobalMap.AtomicObject.applicationId ?? id;

        // Apply transformations...
        ITransformable? newTransformable = null;
        foreach (var mat in localToGlobalMap.Matrix)
        {
          transformable.TransformTo(new Transform() { matrix = mat, units = units }, out newTransformable);
          transformable = newTransformable;
        }

        localToGlobalMap.AtomicObject = (newTransformable as Base)!;
        localToGlobalMap.AtomicObject.id = id;

        // create modified ID and store mapping <- fixes CNX-1707 but causes us material mapping headache!!!
        /*string modifiedAppId = $"{originalAppId}_{Guid.NewGuid().ToString("N")[..8]}";
        if (originalAppId != null)
        {
          originalToModifiedIds[originalAppId] = modifiedAppId;
        }

        localToGlobalMap.AtomicObject.applicationId = modifiedAppId;*/
        localToGlobalMap.Matrix = new HashSet<Matrix4x4>();
      }
    }

    // Update the RenderMaterialProxies with the "new" (aka hacked) application IDs
    if (unpackedRoot.RenderMaterialProxies != null)
    {
      foreach (var proxy in unpackedRoot.RenderMaterialProxies)
      {
        var updatedObjects = new List<string>();
        foreach (var objectId in proxy.objects)
        {
          // Use the modified ID if it exists, otherwise keep the original <- this SUCKS and we need to change
          string idToUse = originalToModifiedIds.TryGetValue(objectId, out var modifiedId) ? modifiedId : objectId;
          updatedObjects.Add(idToUse);
        }
        proxy.objects = updatedObjects;
      }
    }

    // 2 - Bake materials (now with the updated IDs)
    if (unpackedRoot.RenderMaterialProxies != null)
    {
      transactionManager.StartTransaction(true, "Baking materials");
      materialBaker.MapInstanceRenderMaterials(unpackedRoot, localToGlobalMaps);
      materialBaker.MapLayersRenderMaterials(unpackedRoot);
      var map = materialBaker.BakeMaterials(unpackedRoot.RenderMaterialProxies, baseGroupName);
      foreach (var kvp in map)
      {
        revitToHostCacheSingleton.MaterialsByObjectId.Add(kvp.Key, kvp.Value);
      }
      transactionManager.CommitTransaction();
    }

    // 3 - Bake objects
    (
      HostObjectBuilderResult builderResult,
      List<(DirectShape res, string applicationId)> postBakePaintTargets
    ) conversionResults;
    {
      using var _ = activityFactory.Start("Baking objects");
      transactionManager.StartTransaction(true, "Baking objects");

      using (
        converterSettings.Push(currentSettings =>
          currentSettings with
          {
            ReferencePointTransform = CalculateNewTransform(
              currentSettings.ReferencePointTransform,
              referencePointTransformFromRootObject
            )
          }
        )
      )
      {
        conversionResults = BakeObjects(localToGlobalMaps, onOperationProgressed, cancellationToken);
      }
      transactionManager.CommitTransaction();
    }

    // 4 - Paint solids
    /*{
      using var _ = activityFactory.Start("Painting solids");
      transactionManager.StartTransaction(true, "Painting solids");
      PostBakePaint(conversionResults.postBakePaintTargets);
      transactionManager.CommitTransaction();
    }*/

    // 5 - Create group
    {
      using var _ = activityFactory.Start("Grouping");
      transactionManager.StartTransaction(true, "Grouping");
      groupManager.BakeGroupForTopLevel(baseGroupName);
      transactionManager.CommitTransaction();
    }

    return conversionResults.builderResult;
  }

  private Autodesk.Revit.DB.Transform? CalculateNewTransform(
    Autodesk.Revit.DB.Transform? receiveTransform,
    Autodesk.Revit.DB.Transform? rootTransform
  )
  {
    if (receiveTransform == null)
    {
      return rootTransform;
    }

    if (rootTransform == null)
    {
      return receiveTransform;
    }

    return rootTransform.Multiply(receiveTransform);
  }

  private (
    HostObjectBuilderResult builderResult,
    List<(DirectShape res, string applicationId)> postBakePaintTargets
  ) BakeObjects(
    IReadOnlyCollection<LocalToGlobalMap> localToGlobalMaps,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var _ = activityFactory.Start("BakeObjects");
    var conversionResults = new List<ReceiveConversionResult>();
    var bakedObjectIds = new List<string>();
    int count = 0;

    var postBakePaintTargets = new List<(DirectShape res, string applicationId)>();

    foreach (LocalToGlobalMap localToGlobalMap in localToGlobalMaps)
    {
      var ex = conversionHandler.TryConvert(() =>
      {
        cancellationToken.ThrowIfCancellationRequested();
        // actual conversion happens here!
        var result = converter.Convert(localToGlobalMap.AtomicObject);
        onOperationProgressed.Report(new("Converting", (double)++count / localToGlobalMaps.Count));
        if (result is DirectShapeDefinitionWrapper)
        {
          // direct shape creation happens here
          DirectShape directShapes = localToGlobalDirectShapeConverter.Convert(
            (localToGlobalMap.AtomicObject, localToGlobalMap.Matrix)
          );

          bakedObjectIds.Add(directShapes.UniqueId);
          groupManager.AddToTopLevelGroup(directShapes);

          // we need to establish where the "normal route" is, this targets specifically IRawEncodedObject and
          // processes just IRawEncodedObject in maps to create post base paint targets for solids specifically
          // this smells big time.
          // TODO: created material is wrong nonetheless but visually it all looks correct in Revit. Investigate what is going on
          if (localToGlobalMap.AtomicObject is Base myBase)
          {
            SetSolidPostBakePaintTargets(myBase, directShapes, postBakePaintTargets);
          }

          conversionResults.Add(
            new(Status.SUCCESS, localToGlobalMap.AtomicObject, directShapes.UniqueId, "Direct Shape")
          );
        }
        else
        {
          throw new ConversionException($"Failed to cast {result.GetType()} to direct shape definition wrapper.");
        }
      });
      if (ex is not null)
      {
        conversionResults.Add(new(Status.ERROR, localToGlobalMap.AtomicObject, null, null, ex));
      }
    }
    return (new(bakedObjectIds, conversionResults), postBakePaintTargets);
  }

  /// <summary>
  /// We're using this to assign materials to solids coming via the shape importer.
  /// </summary>
  /// <param name="paintTargets"></param>
  private void PostBakePaint(List<(DirectShape res, string applicationId)> paintTargets)
  {
    foreach (var (res, applicationId) in paintTargets)
    {
      var elGeometry = res.get_Geometry(new Options() { DetailLevel = ViewDetailLevel.Undefined });
      var materialId = ElementId.InvalidElementId;
      if (revitToHostCacheSingleton.MaterialsByObjectId.TryGetValue(applicationId, out var mappedElementId))
      {
        materialId = mappedElementId;
      }

      if (materialId == ElementId.InvalidElementId)
      {
        continue;
      }

      // NOTE: some geometries fail to convert as solids, and the api defaults back to meshes (from the shape importer). These cannot be painted, so don't bother.
      foreach (var geo in elGeometry)
      {
        if (geo is Solid s)
        {
          foreach (Face face in s.Faces)
          {
            converterSettings.Current.Document.Paint(res.Id, face, materialId);
          }
        }
      }
    }
  }

  private void PreReceiveDeepClean(string baseGroupName)
  {
    DirectShapeLibrary.GetDirectShapeLibrary(converterSettings.Current.Document).Reset(); // Note: this needs to be cleared, as it is being used in the converter

    revitToHostCacheSingleton.MaterialsByObjectId.Clear(); // Massive hack!
    groupManager.PurgeGroups(baseGroupName);
    materialBaker.PurgeMaterials(baseGroupName);
  }

  public void Dispose() => transactionManager?.Dispose();

  // NOTE: temp poc HACK!
  // this hack only works if we are only assuming one material applied to the solids inside DataObject displayValue. as soon as we have multiple solids with multiple materials it will break again.
  // TODO: clean this up / refactor
  private void SetSolidPostBakePaintTargets(Base baseObj, DirectShape directShapes, List<(DirectShape, string)> targets)
  {
    switch (baseObj)
    {
      case IRawEncodedObject:
        targets.Add((directShapes, baseObj.applicationId ?? baseObj.id.NotNull()));
        break;

      case DataObject dataObj:
        foreach (var item in dataObj.displayValue)
        {
          SetSolidPostBakePaintTargets(item, directShapes, targets);
        }
        break;
    }
  }
}
