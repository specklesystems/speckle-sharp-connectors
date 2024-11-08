using ArcGIS.Core.Geometry;

namespace Speckle.Converters.ArcGIS3.Utils;

/// <summary>
/// Container with origin coordinates and rotation angle
/// </summary>
public readonly struct CRSorigin
{
  public double LatDegrees { get; }
  public double LonDegrees { get; }

  /// <summary>
  /// Initializes a new instance of <see cref="CRSorigin"/>.
  /// </summary>
  /// <param name="latDegrees">Latitude (Y) in degrees.</param>
  /// <param name="lonDegrees">Longitude (X) in degrees.</param>
  public CRSorigin(double latDegrees, double lonDegrees)
  {
    LatDegrees = latDegrees;
    LonDegrees = lonDegrees;
  }

  public SpatialReference CreateCustomCRS()
  {
    string wktString =
      // QGIS example: $"PROJCS[\"unknown\", GEOGCS[\"unknown\", DATUM[\"WGS_1984\", SPHEROID[\"WGS 84\", 6378137, 298.257223563], AUTHORITY[\"EPSG\", \"6326\"]], PRIMEM[\"Greenwich\", 0, AUTHORITY[\"EPSG\", \"8901\"]], UNIT[\"degree\", 0.0174532925199433]], PROJECTION[\"Transverse_Mercator\"], PARAMETER[\"latitude_of_origin\", {LatDegrees}], PARAMETER[\"central_meridian\", {LonDegrees}], PARAMETER[\"scale_factor\", 1], PARAMETER[\"false_easting\", 0], PARAMETER[\"false_northing\", 0], UNIT[\"metre\", 1, AUTHORITY[\"EPSG\", \"9001\"]], AXIS[\"Easting\", EAST], AXIS[\"Northing\", NORTH]]";
      // replicating ArcGIS created custom WKT:
      $"PROJCS[\"SpeckleSpatialReference_latlon_{LatDegrees}_{LonDegrees}\", GEOGCS[\"GCS_WGS_1984\", DATUM[\"D_WGS_1984\", SPHEROID[\"WGS_1984\", 6378137.0, 298.257223563]], PRIMEM[\"Greenwich\", 0.0], UNIT[\"Degree\", 0.0174532925199433]], PROJECTION[\"Transverse_Mercator\"], PARAMETER[\"False_Easting\", 0.0], PARAMETER[\"False_Northing\", 0.0], PARAMETER[\"Central_Meridian\", {LonDegrees}], PARAMETER[\"Scale_Factor\", 1.0], PARAMETER[\"Latitude_Of_Origin\", {LatDegrees}], UNIT[\"Meter\", 1.0]]";

    return SpatialReferenceBuilder.CreateSpatialReference(wktString);
  }
}
