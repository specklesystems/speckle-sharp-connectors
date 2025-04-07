using Rhino.Geometry;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

internal sealed class LocalToGlobalMapHandler
{
  private readonly TraversalContextUnpacker _traversalContextUnpacker;
  public readonly GrasshopperCollectionRebuilder CollectionRebuilder;
  private readonly GrasshopperColorBaker _colorBaker;

  public LocalToGlobalMapHandler(
    TraversalContextUnpacker traversalContextUnpacker,
    GrasshopperCollectionRebuilder collectionRebuilder,
    GrasshopperColorBaker colorBaker
  )
  {
    _traversalContextUnpacker = traversalContextUnpacker;
    _colorBaker = colorBaker;
    CollectionRebuilder = collectionRebuilder;
  }

  /// <summary>
  /// Creates a grasshopper speckle object from a local to global map, and appends it to the collection rebuilder
  /// </summary>
  /// <param name="map"></param>
  ///
  public void CreateGrasshopperObjectFromMap(LocalToGlobalMap map)
  {
    try
    {
      List<GeometryBase> converted = Convert(map.AtomicObject);
      var path = _traversalContextUnpacker.GetCollectionPath(map.TraversalContext).ToList();

      foreach (var matrix in map.Matrix)
      {
        var mat = GrasshopperHelpers.MatrixToTransform(matrix, "meters");
        converted.ForEach(res => res.Transform(mat));
      }

      // get the collection
      SpeckleCollectionWrapper objectCollection = CollectionRebuilder.GetOrCreateSpeckleCollectionFromPath(path);

      // get the name and properties
      SpecklePropertyGroupGoo propertyGroup = new();
      string name = "";
      if (map.AtomicObject is Speckle.Objects.Data.DataObject da)
      {
        propertyGroup.CastFrom(da.properties);
        name = da.name;
      }
      else
      {
        if (map.AtomicObject["properties"] is Dictionary<string, object?> props)
        {
          propertyGroup.CastFrom(props);
        }

        if (map.AtomicObject["name"] is string n)
        {
          name = n;
        }
      }

      // get the color
      Color? color = _colorBaker.Cache.TryGetValue(map.AtomicObject.applicationId ?? "", out var cachedColor)
        ? cachedColor.Item1
        : null;

      // create objects for every value in converted. This is where one to many is not handled very nicely.
      foreach (var geometryBase in converted)
      {
        var gh = new SpeckleObjectWrapper()
        {
          Base = map.AtomicObject,
          Path = path.Select(p => p.name).ToList(),
          Parent = objectCollection,
          GeometryBase = geometryBase,
          Properties = propertyGroup,
          Color = color,
          Name = name
        };

        CollectionRebuilder.AppendSpeckleGrasshopperObject(gh, path);
      }
    }
    catch (ConversionException)
    {
      // TODO
    }
  }

  private List<GeometryBase> Convert(Base input)
  {
    var result = ToSpeckleConversionContext.ToHostConverter.Convert(input);

    return result switch
    {
      GeometryBase geometry => [geometry],
      List<GeometryBase> geometryList => geometryList,
      IEnumerable<(GeometryBase, Base)> fallbackConversionResult
        => fallbackConversionResult.Select(t => t.Item1).ToList(), // note special handling for proxying render materials OR we don't care about revit
      _ => throw new SpeckleException("Failed to convert input to rhino")
    };
  }
}
