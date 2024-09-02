using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;

namespace Speckle.Converters.ArcGIS3;

public class ArcGISConversionSettings : IConverterSettings
{
  public Project Project { get; init; }
  public Map Map { get; init; }
  public Uri SpeckleDatabasePath { get; init; }
  public CRSoffsetRotation ActiveCRSoffsetRotation { get; init; }

  public string SpeckleUnits { get; init; }
}
