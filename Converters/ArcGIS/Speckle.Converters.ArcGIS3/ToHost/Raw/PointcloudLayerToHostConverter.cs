using ArcGIS.Desktop.Mapping;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class PointcloudLayerToHostConverter : ITypedConverter<GisLayer, LasDatasetLayer>
{
  public object Convert(Base target) => Convert((GisLayer)target);

  public LasDatasetLayer Convert(GisLayer target)
  {
    // POC:
    throw new NotImplementedException($"Receiving Pointclouds is not supported");
  }
}
