using ArcGIS.Desktop.Mapping;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.ArcGIS.HostApp;

public class ArcGISLayerUnpacker
{
  public void GetHostObjectCollection(
    MapMember mapMember,
    Base convertedBase,
    Collection rootObjectCollection,
    List<(ILayerContainer, Collection)> nestedGroups
  ) { }
}
