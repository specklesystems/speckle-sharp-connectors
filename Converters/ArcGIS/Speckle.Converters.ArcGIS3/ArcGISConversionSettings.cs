using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;

namespace Speckle.Converters.ArcGIS3;

public record ArcGISConversionSettings(
  Project Project,
  Map Map,
  Uri SpeckleDatabasePath,
  CRSoffsetRotation ActiveCRSoffsetRotation,
  string SpeckleUnits
);
