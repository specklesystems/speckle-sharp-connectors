using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Revit.Async;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Revit.Operations.Receive;

internal sealed class RevitHostObjectBuilder : IHostObjectBuilder, IDisposable
{
  private readonly IRootToHostConverter _converter;
  private readonly ScalingServiceToHost _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;
  private readonly ITransactionManager _transactionManager;
  private readonly ILocalToGlobalUnpacker _localToGlobalUnpacker;
  private readonly LocalToGlobalConverterUtils _localToGlobalConverterUtils;
  private readonly RevitGroupBaker _groupBaker;
  private readonly RevitMaterialBaker _materialBaker;
  private readonly ILogger<RevitHostObjectBuilder> _logger;

  private readonly RootObjectUnpacker _rootObjectUnpacker;
  private readonly ISdkActivityFactory _activityFactory;

  public RevitHostObjectBuilder(
    IRootToHostConverter converter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITransactionManager transactionManager,
    ISdkActivityFactory activityFactory,
    ILocalToGlobalUnpacker localToGlobalUnpacker,
    LocalToGlobalConverterUtils localToGlobalConverterUtils,
    RevitGroupBaker groupManager,
    RevitMaterialBaker materialBaker,
    RootObjectUnpacker rootObjectUnpacker,
    ILogger<RevitHostObjectBuilder> logger,
    RevitToHostCacheSingleton revitToHostCacheSingleton,
    ScalingServiceToHost scalingService
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _transactionManager = transactionManager;
    _localToGlobalUnpacker = localToGlobalUnpacker;
    _localToGlobalConverterUtils = localToGlobalConverterUtils;
    _groupBaker = groupManager;
    _materialBaker = materialBaker;
    _rootObjectUnpacker = rootObjectUnpacker;
    _logger = logger;
    _revitToHostCacheSingleton = revitToHostCacheSingleton;
    _scalingService = scalingService;
    _activityFactory = activityFactory;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  ) =>
    RevitTask.RunAsync(() => BuildSync(rootObject, projectName, modelName, onOperationProgressed, cancellationToken));

  private HostObjectBuilderResult BuildSync(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var baseGroupName = $"Project {projectName}: Model {modelName}"; // TODO: unify this across connectors!

    onOperationProgressed?.Invoke("Converting", null);
    using var activity = _activityFactory.Start("Build");

    // 0 - Clean then Rock n Roll! 🎸
    using TransactionGroup preReceiveCleanTransaction = new(_converterSettings.Current.Document, "Pre-receive clean");
    preReceiveCleanTransaction.Start();
    _transactionManager.StartTransaction(true);

    try
    {
      PreReceiveDeepClean(baseGroupName);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to clean up before receive in Revit");
    }

    using (var _ = _activityFactory.Start("Commit"))
    {
      _transactionManager.CommitTransaction();
      preReceiveCleanTransaction.Assimilate();
    }

    // 1 - Unpack objects and proxies from root commit object
    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);
    var localToGlobalMaps = _localToGlobalUnpacker.Unpack(
      unpackedRoot.DefinitionProxies,
      unpackedRoot.ObjectsToConvert.ToList()
    );

    using TransactionGroup transactionGroup =
      new(_converterSettings.Current.Document, $"Received data from {projectName}");
    transactionGroup.Start();
    _transactionManager.StartTransaction();

    if (unpackedRoot.RenderMaterialProxies != null)
    {
      _materialBaker.MapLayersRenderMaterials(unpackedRoot);
      // NOTE: do not set _contextStack.RenderMaterialProxyCache directly, things stop working. Ogu/Dim do not know why :) not a problem as we hopefully will refactor some of these hacks out.
      var map = _materialBaker.BakeMaterials(unpackedRoot.RenderMaterialProxies, baseGroupName);
      foreach (var kvp in map)
      {
        _revitToHostCacheSingleton.MaterialsByObjectId.Add(kvp.Key, kvp.Value);
      }
    }

    var conversionResults = BakeObjects(localToGlobalMaps, onOperationProgressed, cancellationToken);

    using (var _ = _activityFactory.Start("Commit"))
    {
      _transactionManager.CommitTransaction();
      transactionGroup.Assimilate();
    }

    using TransactionGroup createGroupTransaction = new(_converterSettings.Current.Document, "Creating group");
    createGroupTransaction.Start();
    _transactionManager.StartTransaction(true);

    foreach (var (res, applicationId) in conversionResults.Item2)
    {
      var elGeometry = res.get_Geometry(new Options() { DetailLevel = ViewDetailLevel.Undefined });
      var materialId = ElementId.InvalidElementId;
      if (_revitToHostCacheSingleton.MaterialsByObjectId.TryGetValue(applicationId, out var mappedElementId))
      {
        materialId = mappedElementId;
      }
      // NOTE: some geometries fail to convert as solids, and the api defaults back to meshes (from the shape importer). These cannot be painted, so don't bother.
      foreach (var geo in elGeometry)
      {
        if (geo is Solid s)
        {
          foreach (Face face in s.Faces)
          {
            _converterSettings.Current.Document.Paint(res.Id, face, materialId);
          }
        }
      }
    }

    try
    {
      _groupBaker.BakeGroups(baseGroupName);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to create group after receiving elements in Revit");
    }

    using (var _ = _activityFactory.Start("Commit"))
    {
      _transactionManager.CommitTransaction();
      createGroupTransaction.Assimilate();
    }

    _revitToHostCacheSingleton.MaterialsByObjectId.Clear(); // Massive hack!

    return conversionResults.Item1;
  }

  private (HostObjectBuilderResult, List<(DirectShape res, string applicationId)>) BakeObjects(
    List<LocalToGlobalMap> localToGlobalMaps,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var _ = _activityFactory.Start("BakeObjects");
    var conversionResults = new List<ReceiveConversionResult>();
    var bakedObjectIds = new List<string>();
    int count = 0;

    var toPaintLater = new List<(DirectShape res, string applicationId)>();

    foreach (LocalToGlobalMap localToGlobalMap in localToGlobalMaps)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        using var activity = _activityFactory.Start("BakeObject");
        var result = _converter.Convert(localToGlobalMap.AtomicObject);
        onOperationProgressed?.Invoke("Converting", (double)++count / localToGlobalMaps.Count);

        if (result is List<GeometryObject>)
        {
          DirectShape directShapes = CreateDirectShape(localToGlobalMap);

          bakedObjectIds.Add(directShapes.UniqueId.ToString());
          _groupBaker.AddToGroupMapping(localToGlobalMap.TraversalContext, directShapes);
          if (localToGlobalMap.AtomicObject is IRawEncodedObject && localToGlobalMap.AtomicObject is Base myBase)
          {
            toPaintLater.Add((directShapes, myBase.applicationId ?? myBase.id));
          }
          conversionResults.Add(
            new(Status.SUCCESS, localToGlobalMap.AtomicObject, directShapes.UniqueId, "Direct Shape")
          );
        }
        else
        {
          throw new SpeckleConversionException($"Failed to cast {result.GetType()} to Direct Shape.");
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, localToGlobalMap.AtomicObject, null, null, ex));
      }
    }
    return (new(bakedObjectIds, conversionResults), toPaintLater);
  }

  private Transform ConvertMatrixToRevitTransform(Matrix4x4 matrix, string? units)
  {
    var transform = Transform.Identity;
    if (matrix.M44 == 0 || units is null) // TODO: check units nullability?
    {
      return transform;
    }

    var tX = _scalingService.ScaleToNative(matrix.M14 / matrix.M44, units);
    var tY = _scalingService.ScaleToNative(matrix.M24 / matrix.M44, units);
    var tZ = _scalingService.ScaleToNative(matrix.M34 / matrix.M44, units);
    var t = new XYZ(tX, tY, tZ);

    // basis vectors
    XYZ vX = new(matrix.M11, matrix.M21, matrix.M31);
    XYZ vY = new(matrix.M12, matrix.M22, matrix.M32);
    XYZ vZ = new(matrix.M13, matrix.M23, matrix.M33);

    // apply to new transform
    transform.Origin = t;
    transform.BasisX = vX.Normalize();
    transform.BasisY = vY.Normalize();
    transform.BasisZ = vZ.Normalize();

    // TODO: check below needed?
    // // apply doc transform
    // var docTransform = GetDocReferencePointTransform(Doc);
    // var internalTransform = docTransform.Multiply(_transform);

    return transform;
  }

  private void PreReceiveDeepClean(string baseGroupName)
  {
    _groupBaker.PurgeGroups(baseGroupName);
    _materialBaker.PurgeMaterials(baseGroupName);
  }

  private DirectShape CreateDirectShape(LocalToGlobalMap localToGlobalMap)
  {
    // 1- set ds category
    var category = localToGlobalMap.AtomicObject["category"] as string;
    var dsCategory = BuiltInCategory.OST_GenericModel;
    if (category is string categoryString)
    {
      var res = Enum.TryParse($"OST_{categoryString}", out BuiltInCategory cat);
      if (res)
      {
        var c = Category.GetCategory(_converterSettings.Current.Document, cat);
        if (c is not null && DirectShape.IsValidCategoryId(c.Id, _converterSettings.Current.Document))
        {
          dsCategory = cat;
        }
      }
    }

    // 2 - init DirectShape
    var result = DirectShape.CreateElement(_converterSettings.Current.Document, new ElementId(dsCategory));

    // 3 - Transform the geometries
    Transform combinedTransform = Transform.Identity;

    foreach (Matrix4x4 matrix in localToGlobalMap.Matrix)
    {
      Transform revitTransform = ConvertMatrixToRevitTransform(
        matrix,
        localToGlobalMap.AtomicObject["units"] as string
      );
      combinedTransform = combinedTransform.Multiply(revitTransform);
    }

    var transformedGeometries = DirectShape.CreateGeometryInstance(
      _converterSettings.Current.Document,
      localToGlobalMap.AtomicObject.applicationId ?? localToGlobalMap.AtomicObject.id,
      combinedTransform
    );

    // 4- check for valid geometry
    if (!result.IsValidShape(transformedGeometries))
    {
      _converterSettings.Current.Document.Delete(result.Id);
      throw new SpeckleConversionException("Invalid geometry (eg unbounded curves) found for creating directshape.");
    }

    // 5 - This is where we apply the geometries into direct shape.
    result.SetShape(transformedGeometries);

    return result;
  }

  public void Dispose()
  {
    _transactionManager?.Dispose();
  }
}
