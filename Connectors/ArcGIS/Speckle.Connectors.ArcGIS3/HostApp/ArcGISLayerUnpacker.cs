using Speckle.Connectors.ArcGIS.Extensions;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.ArcGIS.HostApp;

public class ArcGISLayerUnpacker
{
  /// <summary>
  /// Cache of all collections created by unpacked Layer MapMembers. Key is Layer URI.
  /// </summary>
  public Dictionary<string, Collection> CollectionCache { get; } = new();

  /// <summary>
  /// Mapmembers can be layers containing objects, or LayerContainers containing other layers.
  /// Unpacks selected mapMembers and creates their corresponding collection on the root collection.
  /// </summary>
  /// <param name="mapMembers"></param>
  /// <param name="parentCollection"></param>
  /// <returns>List of layers containing objects.</returns>
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
          Collection containerCollection = CreateAndAddMapMemberCollectionToParentCollection(
            mapMember,
            parentCollection
          );
          await UnpackSelectionAsync(container.Layers, containerCollection).ConfigureAwait(false);
          break;

        default:
          Collection collection = CreateAndAddMapMemberCollectionToParentCollection(mapMember, parentCollection);
          objects.Add(mapMember);
          CollectionCache.Add(mapMember.URI, collection);
          break;
      }
    }

    return objects;
  }

  // POC: we are *not* attaching CRS information on each layer, because this is only needed for GIS <-> GIS multiplayer, which is not currently a supported workflow.
  private Collection CreateAndAddMapMemberCollectionToParentCollection(
    ADM.MapMember mapMember,
    Collection parentCollection
  )
  {
    Collection collection =
      new()
      {
        name = mapMember.Name,
        applicationId = mapMember.URI,
        ["type"] = mapMember.GetType().Name
      };

    switch (mapMember)
    {
      case ADM.IDisplayTable displayTable: // get fields from layers that implement IDisplayTable, eg FeatureLayer or StandaloneTable
        collection["fields"] = displayTable.GetFieldsAsDictionary();
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

    parentCollection.elements.Add(collection);
    CollectionCache.Add(mapMember.URI, collection);

    return collection;
  }
}
