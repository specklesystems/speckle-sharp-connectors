using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.Utils;

[GenerateAutoInterface]
public class CrsUtils(IConverterSettingsStore<ArcGISConversionSettings> settingsStore) : ICrsUtils
{
  public IDisposable? FindSetCrsDataOnReceive(Base? rootObj)
  {
    if (rootObj is SGIS.GisLayer vLayer)
    {
      // create Spatial Reference (i.e. Coordinate Reference System - CRS)
      string wktString = string.Empty;
      if (vLayer.crs is not null && vLayer.crs.wkt is not null)
      {
        wktString = vLayer.crs.wkt;
      }

      // ATM, GIS commit CRS is stored per layer, but should be moved to the Root level too, and created once per Receive
      ACG.SpatialReference spatialRef = ACG.SpatialReferenceBuilder.CreateSpatialReference(wktString);

      double trueNorthRadians = System.Convert.ToDouble((vLayer.crs?.rotation == null) ? 0 : vLayer.crs.rotation);
      double latOffset = System.Convert.ToDouble((vLayer.crs?.offset_y == null) ? 0 : vLayer.crs.offset_y);
      double lonOffset = System.Convert.ToDouble((vLayer.crs?.offset_x == null) ? 0 : vLayer.crs.offset_x);
      return settingsStore.Push(x =>
        x with
        {
          ActiveCRSoffsetRotation = new CRSoffsetRotation(spatialRef, latOffset, lonOffset, trueNorthRadians)
        }
      );
    }

    return null;
  }
}
