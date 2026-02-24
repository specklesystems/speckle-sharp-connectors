using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.Converters.Common.ToHost;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoInstanceBaker : IInstanceBaker<IReadOnlyCollection<string>>
{
  private readonly RhinoMaterialBaker _materialBaker;
  private readonly RhinoLayerBaker _layerBaker;
  private readonly RhinoColorBaker _colorBaker;
  private readonly ILogger<RhinoInstanceBaker> _logger;
  private readonly IDataObjectInstanceRegistry _dataObjectInstanceRegistry;

  public RhinoInstanceBaker(
    RhinoLayerBaker layerBaker,
    RhinoMaterialBaker rhinoMaterialBaker,
    RhinoColorBaker colorBaker,
    ILogger<RhinoInstanceBaker> logger,
    IDataObjectInstanceRegistry dataObjectInstanceRegistry
  )
  {
    _layerBaker = layerBaker;
    _materialBaker = rhinoMaterialBaker;
    _colorBaker = colorBaker;
    _logger = logger;
    _dataObjectInstanceRegistry = dataObjectInstanceRegistry;
  }

  /// <summary>
  /// Bakes in the host app doc instances. Assumes constituent atomic objects already present in the host app.
  /// </summary>
  /// <param name="instanceComponents">Instance definitions and instances that need creating.</param>
  /// <param name="applicationIdMap">A dict mapping { original application id -> [resulting application ids post conversion] }</param>
  /// <param name="onOperationProgressed"></param>
  public BakeResult BakeInstances(
    ICollection<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, IReadOnlyCollection<string>> applicationIdMap,
    string baseLayerName,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    // var doc = _contextStack.Current.Document;
    var doc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around

    var sortedInstanceComponents = instanceComponents
      .OrderByDescending(x => x.obj.maxDepth) // Sort by max depth, so we start baking from the deepest element first
      .ThenBy(x => x.obj is InstanceDefinitionProxy ? 0 : 1) // Ensure we bake the deepest definition first, then any instances that depend on it
      .ToList();
    var definitionIdAndApplicationIdMap = new Dictionary<string, int>();

    var count = 0;
    var conversionResults = new HashSet<ReceiveConversionResult>();
    var createdObjectIds = new HashSet<string>();
    var consumedObjectIds = new HashSet<string>();
    foreach (var (layerCollection, instanceOrDefinition) in sortedInstanceComponents)
    {
      onOperationProgressed.Report(new("Converting blocks", (double)++count / sortedInstanceComponents.Count));
      try
      {
        if (instanceOrDefinition is InstanceDefinitionProxy definitionProxy)
        {
          var currentApplicationObjectsIds = definitionProxy
            .objects.Select(x => applicationIdMap.TryGetValue(x, out IReadOnlyCollection<string>? value) ? value : null)
            .Where(x => x is not null)
            .SelectMany(id => id.NotNull())
            .ToHashSet();

          var definitionGeometryList = new List<GeometryBase>();
          var attributes = new List<ObjectAttributes>();

          foreach (var id in currentApplicationObjectsIds)
          {
            var docObject = doc.Objects.FindId(new Guid(id));
            // NOTE: we're here being lenient on incomplete block creation. If a block contains unsupported elements that somehow threw/didn't manage to get baked as atomic objects,
            // we just continue rather than throw on a null when accessing the docObject's Geometry.
            if (docObject is null)
            {
              continue;
            }
            definitionGeometryList.Add(docObject.Geometry);
            attributes.Add(docObject.Attributes);
          }

          // POC: Currently we're relying on the definition name for identification if it's coming from speckle and from which model; could we do something else?
          var defName = $"{definitionProxy.name}-({definitionProxy.applicationId})-{baseLayerName}";
          // we cannot place Block Definitions if we have "/" or "\" in the name
          // https://linear.app/speckle/issue/CNX-2051/cant-create-instances-of-blocks-if-originating-from-speckle-sub-model
          defName = RhinoUtils.CleanBlockDefinitionName(defName);
          var defIndex = doc.InstanceDefinitions.Add(
            defName,
            "No description", // POC: perhaps bring it along from source? We'd need to look at ACAD first
            Point3d.Origin,
            definitionGeometryList,
            attributes
          );

          // POC: check on defIndex -1, means we haven't created anything - this is most likely an unrecoverable error at this stage
          if (defIndex == -1)
          {
            throw new ConversionException("Failed to create an instance definition object.");
          }

          if (definitionProxy.applicationId != null)
          {
            definitionIdAndApplicationIdMap[definitionProxy.applicationId] = defIndex;
          }

          // Rhino deletes original objects on block creation - we should do the same.
          doc.Objects.Delete(currentApplicationObjectsIds.Select(stringId => new Guid(stringId)), false);
          consumedObjectIds.UnionWith(currentApplicationObjectsIds);
          createdObjectIds.RemoveWhere(id => consumedObjectIds.Contains(id)); // in case we've consumed some existing instances
        }

        if (
          instanceOrDefinition is InstanceProxy instanceProxy
          && definitionIdAndApplicationIdMap.TryGetValue(instanceProxy.definitionId, out int index)
        )
        {
          var transform = MatrixToTransform(instanceProxy.transform, instanceProxy.units);
          int layerIndex = _layerBaker.GetLayerIndex(layerCollection, baseLayerName);
          string instanceProxyId = instanceProxy.applicationId ?? instanceProxy.id.NotNull();

          // create attributes
          ObjectAttributes atts = instanceProxy.GetAttributes();
          atts.LayerIndex = layerIndex;
          if (_materialBaker.ObjectIdAndMaterialIndexMap.TryGetValue(instanceProxyId, out int mIndex))
          {
            atts.MaterialIndex = mIndex;
            atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
          }

          if (_colorBaker.ObjectColorsIdMap.TryGetValue(instanceProxyId, out (Color, ObjectColorSource) color))
          {
            atts.ObjectColor = color.Item1;
            atts.ColorSource = color.Item2;
          }

          Guid id = doc.Objects.AddInstanceObject(index, transform, atts);
          if (id == Guid.Empty)
          {
            conversionResults.Add(new(Status.ERROR, instanceProxy, instanceProxyId, "Instance (Block)"));
            continue;
          }

          applicationIdMap[instanceProxyId] = new List<string>() { id.ToString() };
          createdObjectIds.Add(id.ToString());
          conversionResults.Add(new(Status.SUCCESS, instanceProxy, id.ToString(), "Instance (Block)"));

          // link this baked instance back to its DataObject if it came from one (the method handles the check)
          _dataObjectInstanceRegistry.LinkInstanceToDataObject(instanceProxyId, id.ToString());
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to create an instance from proxy");
        conversionResults.Add(new(Status.ERROR, instanceOrDefinition as Base ?? new Base(), null, null, ex));
      }
    }

    return new(createdObjectIds, consumedObjectIds, conversionResults);
  }

  public void PurgeInstances(string namePrefix)
  {
    var currentDoc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around

    // clean name prefix to match how block names are created
    var cleanedPrefix = RhinoUtils.CleanBlockDefinitionName(namePrefix);

    foreach (var definition in currentDoc.InstanceDefinitions)
    {
      if (!definition.IsDeleted && definition.Name.Contains(cleanedPrefix))
      {
        currentDoc.InstanceDefinitions.Delete(definition.Index, true, false);
      }
    }
  }

  private Transform MatrixToTransform(Matrix4x4 matrix, string units)
  {
    var currentDoc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around
    var conversionFactor = Units.GetConversionFactor(units, currentDoc.ModelUnitSystem.ToSpeckleString());

    var t = Transform.Identity;
    t.M00 = matrix.M11;
    t.M01 = matrix.M12;
    t.M02 = matrix.M13;
    t.M03 = matrix.M14 * conversionFactor;

    t.M10 = matrix.M21;
    t.M11 = matrix.M22;
    t.M12 = matrix.M23;
    t.M13 = matrix.M24 * conversionFactor;

    t.M20 = matrix.M31;
    t.M21 = matrix.M32;
    t.M22 = matrix.M33;
    t.M23 = matrix.M34 * conversionFactor;

    t.M30 = matrix.M41;
    t.M31 = matrix.M42;
    t.M32 = matrix.M43;
    t.M33 = matrix.M44;
    return t;
  }
}
