using Rhino.Geometry;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

internal sealed class LocalToGlobalMapHandler
{
  private readonly TraversalContextUnpacker _traversalContextUnpacker;
  public readonly GrasshopperCollectionRebuilder CollectionRebuilder;
  private readonly GrasshopperColorUnpacker _colorUnpacker;
  private readonly GrasshopperMaterialUnpacker _materialUnpacker;

  public LocalToGlobalMapHandler(
    TraversalContextUnpacker traversalContextUnpacker,
    GrasshopperCollectionRebuilder collectionRebuilder,
    GrasshopperColorUnpacker colorUnpacker,
    GrasshopperMaterialUnpacker materialUnpacker
  )
  {
    _traversalContextUnpacker = traversalContextUnpacker;
    _colorUnpacker = colorUnpacker;
    _materialUnpacker = materialUnpacker;
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
      List<(GeometryBase, Base)> converted = SpeckleConversionContext.ConvertToHost(map.AtomicObject);
      var path = _traversalContextUnpacker.GetCollectionPath(map.TraversalContext).ToList();

      foreach (var matrix in map.Matrix)
      {
        var mat = GrasshopperHelpers.MatrixToTransform(matrix, "meters");
        converted.ForEach(res => res.Item1.Transform(mat));
      }

      // get the collection
      SpeckleCollectionWrapper objectCollection = CollectionRebuilder.GetOrCreateSpeckleCollectionFromPath(
        path,
        _colorUnpacker,
        _materialUnpacker
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
      // eg: for a data object with multiple base in display, this will create a speckle object wrapper for every base and store the same parent data object in `Base`
      foreach ((GeometryBase geometryBase, Base original) in converted)
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
          Material = null,
          applicationId = original.applicationId // we want to set the app id of the original base, eg the mesh inside the display value of a revit object, for render materials
        };

        CollectionRebuilder.AppendSpeckleGrasshopperObject(gh, path, _colorUnpacker, _materialUnpacker);
      }
    }
    catch (ConversionException)
    {
      // TODO
    }
  }
}
