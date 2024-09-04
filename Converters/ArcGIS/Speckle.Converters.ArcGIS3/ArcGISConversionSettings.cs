using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;

namespace Speckle.Converters.ArcGIS3;

public record ArcGISConversionSettings
{
  public Project Project { get; init; }
  public Map Map { get; init; }
  public Uri SpeckleDatabasePath { get; init; }
  public CRSoffsetRotation ActiveCRSoffsetRotation { get; init; }

  public string SpeckleUnits { get; init; }
}
