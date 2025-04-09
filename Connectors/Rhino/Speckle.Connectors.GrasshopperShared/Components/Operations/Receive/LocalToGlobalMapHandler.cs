using Rhino.Geometry;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Common.Exceptions;

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
      List<GeometryBase> converted = SpeckleConversionContext.ConvertToHost(map.AtomicObject);
      var path = _traversalContextUnpacker.GetCollectionPath(map.TraversalContext).ToList();

      foreach (var matrix in map.Matrix)
      {
        var mat = GrasshopperHelpers.MatrixToTransform(matrix, "meters");
        converted.ForEach(res => res.Transform(mat));
      }

      // get the collection
      SpeckleCollectionWrapper objectCollection = CollectionRebuilder.GetOrCreateSpeckleCollectionFromPath(
        path,
        _colorBaker
      );

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
          Name = name,
          Color = null,
          applicationId = map.AtomicObject.applicationId
        };

        CollectionRebuilder.AppendSpeckleGrasshopperObject(gh, path, _colorBaker);
      }
    }
    catch (ConversionException)
    {
      // TODO
    }
  }
}
