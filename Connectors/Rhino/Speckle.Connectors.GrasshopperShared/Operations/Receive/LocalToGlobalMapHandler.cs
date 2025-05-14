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
  /// Creates a grasshopper speckle object from a local to global map, and appends it to the collection rebuilder.
  /// POC: TODO: this should decimate dataobjects into their display values, while storing the same properties etc
  /// This is because we don't want to be storing one-to-many maps in the object wrapper, this will complicate mutations
  /// </summary>
  /// <param name="map"></param>
  ///
  public void CreateGrasshopperObjectFromMap(LocalToGlobalMap map)
  {
    try
    {
      List<(GeometryBase, Base)> converted = SpeckleConversionContext.ConvertToHost(map.AtomicObject);

      if (converted.Count == 0)
      {
        return; // TODO: throw?
      }

      // get the units and transform by matrices in the map
      string units = map.AtomicObject["units"] is string u
        ? u
        : converted.First().Item2["units"] is string convertedU
          ? convertedU
          : "none";

      foreach (var matrix in map.Matrix)
      {
        var mat = GrasshopperHelpers.MatrixToTransform(matrix, units);
        converted.ForEach(res => res.Item1.Transform(mat));
      }

      // get the collection
      var path = _traversalContextUnpacker.GetCollectionPath(map.TraversalContext).ToList();
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
        if (map.AtomicObject[Constants.PROPERTIES_PROP] is Dictionary<string, object?> props)
        {
          propertyGroup.CastFrom(props);
        }

        if (map.AtomicObject[Constants.NAME_PROP] is string n)
        {
          name = n;
        }
      }

      // create objects for every value in converted. This is where one to many is not handled very nicely.
      // we will decimate dataobjects and multi-object display values here
      // meaning for every value in the display value, we will create a grasshopper wrapper, while preserving app id, name, props, etc
      // similar objects will be re-packaged on send
      foreach ((GeometryBase geometryBase, Base original) in converted)
      {
        var gh = new SpeckleObjectWrapper()
        {
          Base = original,
          Path = path.Select(p => p.name).ToList(),
          Parent = objectCollection,
          GeometryBase = geometryBase,
          Properties = propertyGroup,
          Name = name,
          Color = null,
          Material = null,
          WrapperGuid = map.AtomicObject.applicationId,
          ApplicationId = original.applicationId ?? Guid.NewGuid().ToString() // create if none
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
