using Speckle.Connectors.ArcGIS.HostApp.Extensions;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.ArcGIS.HostApp;

public class ArcGISLayerUnpacker
{
  /// <summary>
  /// Cache of all collections created by unpacked Layer MapMembers. Key is the Speckle applicationId (Layer URI).
  /// </summary>
  public Dictionary<string, Collection> CollectionCache { get; } = new();

  public List<ADM.MapMember> RemoveNestedMapMembers(IReadOnlyList<ADM.MapMember> mapMembers)
  {
    List<ADM.MapMember> rootLayers = new();
    foreach (ADM.MapMember mapMember in mapMembers)
    {
      if (mapMember is ADM.Layer layer)
      {
        if (layer.Parent is ADM.Map)
        {
          rootLayers.Add(mapMember);
        }
      }
    }

    return rootLayers;
  }

  /// <summary>
  /// Mapmembers can be layers containing objects, or LayerContainers containing other layers.
  /// Unpacks selected mapMembers and creates their corresponding collection on the root collection.
  /// </summary>
  /// <param name="mapMembers"></param>
  /// <param name="parentCollection"></param>
  /// <returns>List of layers containing objects.</returns>
  /// <exception cref="AC.CalledOnWrongThreadException">Thrown when this method is *not* called on the MCT, because this method accesses mapmember fields</exception>
  public async Task<List<ADM.MapMember>> UnpackSelectionAsync(
    IReadOnlyList<ADM.MapMember> mapMembers,
    Collection parentCollection
  )
  {
    List<ADM.MapMember> objects = new();

    foreach (ADM.MapMember mapMember in mapMembers)
    {
      switch (mapMember)
      {
        case ADM.ILayerContainer container:
          Collection containerCollection = CreateAndCacheMapMemberCollection(mapMember, true);
          parentCollection.elements.Add(containerCollection);

          List<ADM.MapMember> unpackedLayers = await UnpackSelectionAsync(container.Layers, containerCollection)
            .ConfigureAwait(false);
          objects.AddRange(unpackedLayers);
          break;

        default:
          Collection collection = CreateAndCacheMapMemberCollection(mapMember);
          parentCollection.elements.Add(collection);
          objects.Add(mapMember);
          break;
      }
    }

    return objects;
  }

  private Collection CreateAndCacheMapMemberCollection(ADM.MapMember mapMember, bool isLayerContainer = false)
  {
    string mapMemberApplicationId = mapMember.GetSpeckleApplicationId();
    Collection collection =
      new()
      {
        name = mapMember.Name,
        applicationId = mapMemberApplicationId,
        ["type"] = mapMember.GetType().Name
      };

    switch (mapMember)
    {
      case ADM.IDisplayTable displayTable: // get fields from layers that implement IDisplayTable, eg FeatureLayer or StandaloneTable
        Dictionary<string, string>? fields = displayTable
          .GetFieldDescriptions()
          .ToDictionary(field => field.Name, field => field.Type.ToString());
        collection["fields"] = fields;
        if (mapMember is ADM.BasicFeatureLayer basicFeatureLayer)
        {
          collection["shapeType"] = basicFeatureLayer.ShapeType.ToString();
        }
        break;

      case ADM.Layer layer:
        collection["mapLayerType"] = layer.MapLayerType.ToString();
        break;

      case ADM.ILayerContainer:
      default:
        break;
    }

    if (!isLayerContainer) // do not cache layer containers, since these won't contain any objects
    {
      CollectionCache.Add(mapMemberApplicationId, collection);
    }

    return collection;
  }
}
