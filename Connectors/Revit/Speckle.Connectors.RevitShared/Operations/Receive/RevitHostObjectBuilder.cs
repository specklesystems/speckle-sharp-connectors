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
using Speckle.Objects.Data;
using Speckle.Objects.Geometry;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Revit.Operations.Receive;

public sealed class RevitHostObjectBuilder(
  IRootToHostConverter converter,
  IConverterSettingsStore<RevitConversionSettings> converterSettings,
  ITransactionManager transactionManager,
  ISdkActivityFactory activityFactory,
  RevitGroupBaker groupManager,
  RevitMaterialBaker materialBaker,
  RootObjectUnpacker rootObjectUnpacker,
  ILogger<RevitHostObjectBuilder> logger,
  IThreadContext threadContext,
  RevitToHostCacheSingleton revitToHostCacheSingleton,
  ITypedConverter<
    (Base atomicObject, IReadOnlyCollection<Matrix4x4> matrix, DataObject? parentDataObject),
    DirectShape
  > localToGlobalDirectShapeConverter,
  IReceiveConversionHandler conversionHandler,
  RevitFamilyBaker familyBaker,
  DirectShapeUnpackStrategy directShapeUnpackStrategy,
  FamilyUnpackStrategy familyUnpackStrategy,
  RevitPreBakeSetupService preBakeSetupService
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
    Autodesk.Revit.DB.Transform? referencePointTransformFromRootObject = null;
    if (
      rootObject.DynamicPropertyKeys.Contains(RootKeys.REFERENCE_POINT_TRANSFORM)
      && rootObject[RootKeys.REFERENCE_POINT_TRANSFORM] is Dictionary<string, object> transformDict
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

    // 2 - Determine conversion path based on setting
    var receiveInstancesAsFamilies = converterSettings.Current.ReceiveInstancesAsFamilies;
    IRevitUnpackStrategy unpackStrategy = receiveInstancesAsFamilies ? familyUnpackStrategy : directShapeUnpackStrategy;

    // 3 - Split objects/Flatten objects based on strategy
    var unpackResult = unpackStrategy.Unpack(unpackedRoot);

    // 4 - Apply ID modifications and bake materials
    preBakeSetupService.ApplyIdModificationsAndBakeMaterials(unpackResult, unpackedRoot);

    // 5 - Bake objects
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
        conversionResults = BakeObjects(
          unpackResult.LocalToGlobalMaps,
          unpackResult.ParentDataObjectMap,
          onOperationProgressed,
          cancellationToken
        );
      }

      transactionManager.CommitTransaction();
    }

    // Bakes instances as families (if setting is enabled Count > 0)
    if (receiveInstancesAsFamilies && unpackResult.InstanceComponents is { Count: > 0 })
    {
      var speckleObjectLookup = new Dictionary<string, TraversalContext>();
      foreach (var tc in unpackedRoot.ObjectsToConvert)
      {
        var obj = tc.Current;

        // 1. Primary Index: our Hash
        // TODO: investigate. this should never be null? but i (Björn) had some weird edge-cases
        if (!string.IsNullOrEmpty(obj.id))
        {
          speckleObjectLookup[obj.id.NotNullOrWhiteSpace()] = tc;
        }

        // 2. Secondary Index: Application ID (kinda fallback)
        if (!string.IsNullOrEmpty(obj.applicationId))
        {
          speckleObjectLookup[obj.applicationId.NotNullOrWhiteSpace()] = tc;
        }
      }

      // Pass the unpacked material proxies down to the family baker, defaulting to empty if null
      var materialProxies = unpackedRoot.RenderMaterialProxies ?? [];

      conversionResults = BakeInstancesAsFamilies(
        unpackResult.InstanceComponents,
        conversionResults,
        speckleObjectLookup,
        materialProxies,
        onOperationProgressed
      );
    }

    // 6 - Paint solids
    {
      using var _ = activityFactory.Start("Painting solids");
      transactionManager.StartTransaction(true, "Painting solids");
      PostBakePaint(conversionResults.postBakePaintTargets);
      transactionManager.CommitTransaction();
    }

    // 7 - Create group
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
  ) BakeInstancesAsFamilies(
    List<(Collection[] path, IInstanceComponent component)> instanceComponents,
    (
      HostObjectBuilderResult builderResult,
      List<(DirectShape res, string applicationId)> postBakePaintTargets
    ) currentResults,
    Dictionary<string, TraversalContext> speckleObjectLookup,
    IReadOnlyCollection<RenderMaterialProxy> materialProxies,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    using var _ = activityFactory.Start("Creating families");
    transactionManager.StartTransaction(true, "Creating families");

    (List<ReceiveConversionResult> familyResults, List<string> familyElementIds) = familyBaker.BakeInstances(
      instanceComponents,
      speckleObjectLookup,
      materialProxies,
      onOperationProgressed
    );

    // Merge results
    var mergedConversionResults = currentResults.builderResult.ConversionResults.ToList();
    mergedConversionResults.AddRange(familyResults);

    var mergedBakedObjectIds = currentResults.builderResult.BakedObjectIds.ToList();
    mergedBakedObjectIds.AddRange(familyElementIds);

    // Add created elements to group
    foreach (var elementId in familyElementIds)
    {
      var element = converterSettings.Current.Document.GetElement(elementId);
      if (element != null)
      {
        groupManager.AddToTopLevelGroup(element);
      }
    }

    transactionManager.CommitTransaction();

    return (
      new HostObjectBuilderResult(mergedBakedObjectIds, mergedConversionResults),
      currentResults.postBakePaintTargets
    );
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
    Dictionary<string, DataObject> parentDataObjectMap,
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
          // Look up parent DataObject for this atomic object (handles InstanceProxy displayValue)
          var atomicId = localToGlobalMap.AtomicObject.applicationId;
          DataObject? parentDataObject = null;
          if (atomicId is not null)
          {
            parentDataObjectMap.TryGetValue(atomicId, out parentDataObject);
          }

          // direct shape creation happens here
          DirectShape directShapes = localToGlobalDirectShapeConverter.Convert(
            (localToGlobalMap.AtomicObject, localToGlobalMap.Matrix, parentDataObject)
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

    revitToHostCacheSingleton.Clear(); // "Massive hack!" - Anonymous. Ogu and Björn: it looks legit
    groupManager.PurgeGroups(baseGroupName);
    materialBaker.PurgeMaterials(baseGroupName);
  }

  public void Dispose() => transactionManager.Dispose();

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
