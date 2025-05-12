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
  > localToGlobalDirectShapeConverter
) : IHostObjectBuilder, IDisposable
{
  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  ) =>
    threadContext.RunOnMainAsync(
      () => Task.FromResult(BuildSync(rootObject, projectName, modelName, onOperationProgressed, cancellationToken))
    );

  private HostObjectBuilderResult BuildSync(
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    // TODO: formalise getting transform info from rootObject. this dict access is gross.
    Autodesk.Revit.DB.Transform? referencePointTransform = null;
    if (
      rootObject.DynamicPropertyKeys.Contains(ReferencePointHelper.REFERENCE_POINT_TRANSFORM_KEY)
      && rootObject[ReferencePointHelper.REFERENCE_POINT_TRANSFORM_KEY] is Dictionary<string, object> transformDict
      && transformDict.TryGetValue("transform", out var transformValue)
    )
    {
      referencePointTransform = ReferencePointHelper.GetTransformFromRootObject(transformValue);
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

    // 2 - Bake materials
    if (unpackedRoot.RenderMaterialProxies != null)
    {
      transactionManager.StartTransaction(true, "Baking materials");
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
            ReferencePointTransform = referencePointTransform
          }
        )
      )
      {
        conversionResults = BakeObjects(localToGlobalMaps, onOperationProgressed, cancellationToken);
      }
      transactionManager.CommitTransaction();
    }

    // 4 - Paint solids
    {
      using var _ = activityFactory.Start("Painting solids");
      transactionManager.StartTransaction(true, "Painting solids");
      PostBakePaint(conversionResults.postBakePaintTargets);
      transactionManager.CommitTransaction();
    }

    // 5 - Create group
    {
      using var _ = activityFactory.Start("Grouping");
      transactionManager.StartTransaction(true, "Grouping");
      groupManager.BakeGroupForTopLevel(baseGroupName);
      transactionManager.CommitTransaction();
    }

    return conversionResults.builderResult;
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
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        using var activity = activityFactory.Start("BakeObject");

        // POC hack of the ages: try to pre transform curves, points and meshes before baking
        // we need to bypass the local to global converter as there we don't have access to what we want. that service will/should stop existing.
        if (
          localToGlobalMap.AtomicObject is ITransformable transformable // and ICurve
          && localToGlobalMap.Matrix.Count > 0
          && localToGlobalMap.AtomicObject["units"] is string units
        )
        {
          //TODO TransformTo will be deprecated as it's dangerous and requires ID transposing which is wrong!
          //ID needs to be copied to the new instance
          var id = localToGlobalMap.AtomicObject.id;
          var originalAppId = localToGlobalMap.AtomicObject.applicationId;

          ITransformable? newTransformable = null;
          foreach (var mat in localToGlobalMap.Matrix)
          {
            transformable.TransformTo(new Transform() { matrix = mat, units = units }, out newTransformable);
            transformable = newTransformable;
          }

          localToGlobalMap.AtomicObject = (newTransformable as Base)!;
          localToGlobalMap.AtomicObject.id = id;

          // Make applicationId unique by appending a short GUID
          // This prevents DirectShapeLibrary from using the same definition for multiple instances
          localToGlobalMap.AtomicObject.applicationId = $"{originalAppId ?? id}_{Guid.NewGuid().ToString("N")[..8]}"; // hack of all of the ages. related to CNX-1707
          localToGlobalMap.Matrix = new HashSet<Matrix4x4>(); // flush out the list, as we've applied the transforms already
        }

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

          // we need to establish where the "normal route" is, this targets specifically IRawEncodedObject
          // processes just IRawEncodedObject in maps to create post base paint targets for solids specifically
          // this smells
          // TODO: created material is wrong nonetheless but visually it all looks correct in Revit. Investigate what is going on
          if (localToGlobalMap.AtomicObject is Base myBase)
          {
            if (myBase is IRawEncodedObject)
            {
              postBakePaintTargets.Add((directShapes, myBase.applicationId ?? myBase.id.NotNull()));
            }
            else if (myBase is DataObject da) // hack
            {
              foreach (Base @base in da.displayValue)
              {
                if (@base is IRawEncodedObject)
                {
                  postBakePaintTargets.Add((directShapes, @base.applicationId ?? myBase.id.NotNull()));
                }
              }
            }
          }

          conversionResults.Add(
            new(Status.SUCCESS, localToGlobalMap.AtomicObject, directShapes.UniqueId, "Direct Shape")
          );
        }
        else
        {
          throw new ConversionException($"Failed to cast {result.GetType()} to direct shape definition wrapper.");
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, localToGlobalMap.AtomicObject, null, null, ex));
        logger.LogError(ex, $"Failed to convert object of type {localToGlobalMap.AtomicObject.speckle_type}");
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
}
