using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
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

  /// <summary>
  /// The layer cache storing the full name of created layers.
  /// Full names should be stored in all lowercase due to case-agnostic layer name uniqueness requirements in Rhino.
  /// TryGetValue on this dict should also test for lowercase-only names.
  /// </summary>
  /// <remarks>The case-agnostic requirement applies to some models (eg Revit with linked models) that may have multiple collections with the same name but with different capitalizations.</remarks>
  private readonly Dictionary<string, int> _hostLayerCache = new();

  private static readonly string s_pathSeparator =
#if RHINO8_OR_GREATER
  ModelComponent.NamePathSeparator;
#else
  Layer.PathSeparator;
#endif

  public RhinoLayerBaker(RhinoMaterialBaker materialBaker, RhinoColorBaker colorBaker)
  {
    _materialBaker = materialBaker;
    _colorBaker = colorBaker;
  }

  /// <summary>
  /// Creates the base layer and adds it to the cache.
  /// </summary>
  /// <param name="baseLayerName"></param>
  private void CreateBaseLayer(string baseLayerName)
  {
    var index = RhinoDoc.ActiveDoc.Layers.Add(new Layer { Name = baseLayerName }); // POC: too much effort right now to wrap around the interfaced layers and doc
    _hostLayerCache[baseLayerName.ToLower()] = index;
  }

  /// <summary>
  /// Creates all layers needed for receiving data.
  /// </summary>
  /// <param name="paths">Collections of paths</param>
  /// <param name="baseLayerName">Name of the base layer</param>
  /// <remarks>Make sure this is executing on the main thread, using e.g RhinoApp.InvokeAndWait.</remarks>
  public void CreateAllLayersForReceive(IEnumerable<Collection[]> paths, string baseLayerName)
  {
    try
    {
      CreateBaseLayer(baseLayerName);
      var uniquePaths = new Dictionary<string, Collection[]>();
      foreach (var path in paths)
      {
        var names = path.Select(o => string.IsNullOrWhiteSpace(o.name) ? "unnamed" : o.name);
        var key = string.Join(",", names);
        uniquePaths[key] = path;
      }

      foreach (var uniquePath in uniquePaths)
      {
        CreateLayerFromPath(uniquePath.Value, baseLayerName);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      throw new SpeckleException("Could not create all layers for receive.", ex);
    }
  }

  /// <summary>
  /// Retrieves the index of a layer based on the given collection path and base layer name.
  /// </summary>
  /// <param name="collectionPath">The array containing the collection path to the layer.</param>
  /// <param name="baseLayerName">The name of the base layer.</param>
  /// <returns>The index of the layer in the cache.</returns>
  /// <exception cref="SpeckleException">Thrown when the layer is not found in the cache. This can happen if you didn't call previously <see cref="CreateAllLayersForReceive"/></exception>
  public int GetLayerIndex(Collection[] collectionPath, string baseLayerName)
  {
    var layerPath = collectionPath
      .Select(o => string.IsNullOrWhiteSpace(o.name) ? "unnamed" : RhinoUtils.CleanLayerName(o.name))
      .Prepend(baseLayerName);

    var layerFullName = string.Join(s_pathSeparator, layerPath);

    if (_hostLayerCache.TryGetValue(layerFullName.ToLower(), out int existingLayerIndex))
    {
      return existingLayerIndex;
    }

    throw new ConversionException($"Did not find a layer in the cache with the name '{layerFullName}'");
  }

  /// <summary>
  /// Creates a layer based on the given collection path and adds it to the Rhino document.
  /// </summary>
  /// <param name="collectionPath">An array of Collection objects representing the path to create the layer.</param>
  /// <param name="baseLayerName">The base layer name to start creating the new layer.</param>
  /// <returns>The index of the last created layer.</returns>
  private int CreateLayerFromPath(Collection[] collectionPath, string baseLayerName)
  {
    var currentLayerName = baseLayerName;
    var currentDocument = RhinoDoc.ActiveDoc; // POC: too much effort right now to wrap around the interfaced layers
    Layer? previousLayer = currentDocument.Layers.FindName(currentLayerName);
    foreach (Collection collection in collectionPath)
    {
      currentLayerName +=
        s_pathSeparator
        + (string.IsNullOrWhiteSpace(collection.name) ? "unnamed" : RhinoUtils.CleanLayerName(collection.name));

      if (_hostLayerCache.TryGetValue(currentLayerName.ToLower(), out int value))
      {
        previousLayer = currentDocument.Layers.FindIndex(value);
        continue;
      }

      var cleanNewLayerName = RhinoUtils.CleanLayerName(collection.name);
      Layer newLayer = new() { Name = cleanNewLayerName, ParentLayerId = previousLayer?.Id ?? Guid.Empty };

      // set material
      if (
        _materialBaker.ObjectIdAndMaterialIndexMap.TryGetValue(
          collection.applicationId ?? collection.id.NotNull(),
          out int mIndex
        )
      )
      {
        newLayer.RenderMaterialIndex = mIndex;
      }

      // set color
      if (
        _colorBaker.ObjectColorsIdMap.TryGetValue(
          collection.applicationId ?? collection.id.NotNull(),
          out (Color, ObjectColorSource) color
        )
      )
      {
        newLayer.Color = color.Item1;
      }

      int index = currentDocument.Layers.Add(newLayer);
      if (index == -1)
      {
        throw new SpeckleException($"Could not create layer '{currentLayerName}'.");
      }

      _hostLayerCache.Add(currentLayerName.ToLower(), index);
      previousLayer = currentDocument.Layers.FindIndex(index); // note we need to get the correct id out, hence why we're double calling this
    }

    return previousLayer.Index;
  }
}
