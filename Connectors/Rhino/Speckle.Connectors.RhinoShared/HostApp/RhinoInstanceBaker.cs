﻿using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoInstanceBaker : IInstanceBaker<List<string>>
{
  private readonly RhinoMaterialBaker _materialBaker;
  private readonly RhinoLayerBaker _layerBaker;
  private readonly RhinoColorBaker _colorBaker;
  private readonly ILogger<RhinoInstanceBaker> _logger;

  public RhinoInstanceBaker(
    RhinoLayerBaker layerBaker,
    RhinoMaterialBaker rhinoMaterialBaker,
    RhinoColorBaker colorBaker,
    ILogger<RhinoInstanceBaker> logger
  )
  {
    _layerBaker = layerBaker;
    _materialBaker = rhinoMaterialBaker;
    _colorBaker = colorBaker;
    _logger = logger;
  }

  /// <summary>
  /// Bakes in the host app doc instances. Assumes constituent atomic objects already present in the host app.
  /// </summary>
  /// <param name="instanceComponents">Instance definitions and instances that need creating.</param>
  /// <param name="applicationIdMap">A dict mapping { original application id -> [resulting application ids post conversion] }</param>
  /// <param name="onOperationProgressed"></param>
  public async Task<BakeResult> BakeInstances(
    IReadOnlyCollection<(Collection[] collectionPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, List<string>> applicationIdMap,
    string baseLayerName,
    ProgressAction onOperationProgressed
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
    var conversionResults = new List<ReceiveConversionResult>();
    var createdObjectIds = new List<string>();
    var consumedObjectIds = new List<string>();
    foreach (var (layerCollection, instanceOrDefinition) in sortedInstanceComponents)
    {
      await onOperationProgressed
        .Invoke("Converting blocks", (double)++count / sortedInstanceComponents.Count)
        .ConfigureAwait(false);
      try
      {
        if (instanceOrDefinition is InstanceDefinitionProxy definitionProxy)
        {
          var currentApplicationObjectsIds = definitionProxy
            .objects.Select(x => applicationIdMap.TryGetValue(x, out List<string>? value) ? value : null)
            .Where(x => x is not null)
            .SelectMany(id => id.NotNull())
            .ToList();

          var definitionGeometryList = new List<GeometryBase>();
          var attributes = new List<ObjectAttributes>();

          foreach (var id in currentApplicationObjectsIds)
          {
            var docObject = doc.Objects.FindId(new Guid(id));
            definitionGeometryList.Add(docObject.Geometry);
            attributes.Add(docObject.Attributes);
          }

          // POC: Currently we're relying on the definition name for identification if it's coming from speckle and from which model; could we do something else?
          var defName = $"{definitionProxy.name}-({definitionProxy.applicationId})-{baseLayerName}";
          var defIndex = doc.InstanceDefinitions.Add(
            defName,
            "No description", // POC: perhaps bring it along from source? We'd need to look at ACAD first
            Point3d.Origin,
            definitionGeometryList,
            attributes
          );

          // POC: check on defIndex -1, means we haven't created anything - this is most likely an recoverable error at this stage
          if (defIndex == -1)
          {
            throw new ConversionException("Failed to create an instance defintion object.");
          }

          if (definitionProxy.applicationId != null)
          {
            definitionIdAndApplicationIdMap[definitionProxy.applicationId] = defIndex;
          }

          // Rhino deletes original objects on block creation - we should do the same.
          doc.Objects.Delete(currentApplicationObjectsIds.Select(stringId => new Guid(stringId)), false);
          consumedObjectIds.AddRange(currentApplicationObjectsIds);
          createdObjectIds.RemoveAll(id => consumedObjectIds.Contains(id)); // in case we've consumed some existing instances
        }

        if (
          instanceOrDefinition is InstanceProxy instanceProxy
          && definitionIdAndApplicationIdMap.TryGetValue(instanceProxy.definitionId, out int index)
        )
        {
          var transform = MatrixToTransform(instanceProxy.transform, instanceProxy.units);

          // POC: having layer creation during instance bake means no render materials!!
          int layerIndex = _layerBaker.GetAndCreateLayerFromPath(layerCollection, baseLayerName);

          string instanceProxyId = instanceProxy.applicationId ?? instanceProxy.id;

          ObjectAttributes atts = new() { LayerIndex = layerIndex };
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
    foreach (var definition in currentDoc.InstanceDefinitions)
    {
      if (!definition.IsDeleted && definition.Name.Contains(namePrefix))
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
