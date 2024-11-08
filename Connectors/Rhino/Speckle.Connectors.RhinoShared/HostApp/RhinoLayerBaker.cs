using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;
using Layer = Rhino.DocObjects.Layer;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing layer creation. Expects to be a scoped dependency per receive operation.
/// </summary>
public class RhinoLayerBaker : TraversalContextUnpacker
{
  private readonly RhinoMaterialBaker _materialBaker;
  private readonly RhinoColorBaker _colorBaker;
  private readonly Dictionary<string, int> _hostLayerCache = new();

  public RhinoLayerBaker(RhinoMaterialBaker materialBaker, RhinoColorBaker colorBaker)
  {
    _materialBaker = materialBaker;
    _colorBaker = colorBaker;
  }

  /// <summary>
  /// Creates the base layer and adds it to the cache.
  /// </summary>
  /// <param name="baseLayerName"></param>
  public void CreateBaseLayer(string baseLayerName)
  {
    var index = RhinoDoc.ActiveDoc.Layers.Add(new Layer { Name = baseLayerName }); // POC: too much effort right now to wrap around the interfaced layers and doc
    _hostLayerCache[baseLayerName] = index;
  }

  public void CreateAllLayersForReceive(IEnumerable<Collection[]> paths, string baseLayerName)
  {
    CreateBaseLayer(baseLayerName);
    var uniquePaths = new Dictionary<string, Collection[]>();
    foreach (var path in paths)
    {
      var names = path.Select(o => string.IsNullOrWhiteSpace(o.name) ? "unnamed" : o.name);
      var key = string.Join(",", names!);
      uniquePaths[key] = path;
    }

    foreach (var uniquePath in uniquePaths)
    {
      var layerIndex = CreateLayerFromPath(uniquePath.Value, baseLayerName);
    }
  }

  public int GetLayerIndex(Collection[] collectionPath, string baseLayerName)
  {
    var layerPath = collectionPath
      .Select(o => string.IsNullOrWhiteSpace(o.name) ? "unnamed" : o.name)
      .Prepend(baseLayerName);

    var layerFullName = string.Join(Layer.PathSeparator, layerPath);

    if (_hostLayerCache.TryGetValue(layerFullName, out int existingLayerIndex))
    {
      return existingLayerIndex;
    }

    throw new SpeckleNonUserFacingException("Did not find a layer in the cache.");
  }

  /// <summary>
  /// <para>For receive: Use this method to construct layers in the host app when receiving. It progressively caches layers while creating them, so a second call to get the same layer will be fast.</para>
  /// </summary>
  private int CreateLayerFromPath(Collection[] collectionPath, string baseLayerName)
  {
    var currentLayerName = baseLayerName;
    var currentDocument = RhinoDoc.ActiveDoc; // POC: too much effort right now to wrap around the interfaced layers
    Layer? previousLayer = currentDocument.Layers.FindName(currentLayerName);
    foreach (Collection collection in collectionPath)
    {
      currentLayerName += Layer.PathSeparator + collection.name;
      currentLayerName = currentLayerName.Replace("{", "").Replace("}", ""); // Rhino specific cleanup for gh (see RemoveInvalidRhinoChars)
      if (_hostLayerCache.TryGetValue(currentLayerName, out int value))
      {
        previousLayer = currentDocument.Layers.FindIndex(value);
        continue;
      }

      var cleanNewLayerName = collection.name.Replace("{", "").Replace("}", "");
      Layer newLayer = new() { Name = cleanNewLayerName, ParentLayerId = previousLayer?.Id ?? Guid.Empty };

      // set material
      if (
        _materialBaker.ObjectIdAndMaterialIndexMap.TryGetValue(
          collection.applicationId ?? collection.id,
          out int mIndex
        )
      )
      {
        newLayer.RenderMaterialIndex = mIndex;
      }

      // set color
      if (
        _colorBaker.ObjectColorsIdMap.TryGetValue(
          collection.applicationId ?? collection.id,
          out (Color, ObjectColorSource) color
        )
      )
      {
        newLayer.Color = color.Item1;
      }

      int index = currentDocument.Layers.Add(newLayer);
      _hostLayerCache.Add(currentLayerName, index);
      previousLayer = currentDocument.Layers.FindIndex(index); // note we need to get the correct id out, hence why we're double calling this
    }

    return previousLayer.Index;
  }
}
