using System.Xml.Linq;
using ArcGIS.Desktop.Mapping;
using Speckle.Sdk.Common;

namespace Speckle.Converters.ArcGIS3.Utils;

/// <summary>
/// Container with origin offsets and rotation angle for the specified SpatialReference
/// Offsets and rotation will modify geometry on Send, so non-GIS apps can receive it correctly
/// Receiving GIS geometry in GIS hostApp will "undo" the geometry modifications according to the offsets and rotation applied before
/// In the future, CAD/BIM objects will contain ProjectInfo data with CRS and offsets, so this object can be generated on Recieve
/// TODO: consider how to generate this object to receive non-GIS data already now, without it having ProjectInfo object
/// </summary>
public struct CRSoffsetRotation
{
  public ACG.SpatialReference SpatialReference { get; }
  public double LatOffset { get; set; }
  public double LonOffset { get; set; }
  public double TrueNorthRadians { get; set; }

  public SOG.Point OffsetRotateOnReceive(SOG.Point pointOriginal, string speckleUnitString)
  {
    // scale point to match units of the SpatialReference
    string originalUnits = pointOriginal.units;
    SOG.Point point = ScalePoint(pointOriginal, originalUnits, speckleUnitString);

    // 1. rotate coordinates
    NormalizeAngle();
    double x2 = point.x * Math.Cos(TrueNorthRadians) - point.y * Math.Sin(TrueNorthRadians);
    double y2 = point.x * Math.Sin(TrueNorthRadians) + point.y * Math.Cos(TrueNorthRadians);
    // 2. offset coordinates
    x2 += LonOffset;
    y2 += LatOffset;
    SOG.Point movedPoint = new(x2, y2, point.z, speckleUnitString);

    return movedPoint;
  }

  public SOG.Point OffsetRotateOnSend(SOG.Point point, string speckleUnitString)
  {
    // scale point to match units of the SpatialReference
    string originalUnits = point.units;
    point = ScalePoint(point, originalUnits, speckleUnitString);

    // 1. offset coordinates
    NormalizeAngle();
    double x = point.x - LonOffset;
    double y = point.y - LatOffset;
    // 2. rotate coordinates
    double x2 = x * Math.Cos(TrueNorthRadians) + y * Math.Sin(TrueNorthRadians);
    double y2 = -x * Math.Sin(TrueNorthRadians) + y * Math.Cos(TrueNorthRadians);
    SOG.Point movedPoint = new(x2, y2, point.z, speckleUnitString);

    return movedPoint;
  }

  private readonly SOG.Point ScalePoint(SOG.Point point, string fromUnit, string toUnit)
  {
    double scaleFactor = Units.GetConversionFactor(fromUnit, toUnit);
    return new SOG.Point(point.x * scaleFactor, point.y * scaleFactor, point.z * scaleFactor, toUnit);
  }

  private void NormalizeAngle()
  {
    if (TrueNorthRadians < -2 * Math.PI || TrueNorthRadians > 2 * Math.PI)
    {
      TrueNorthRadians %= 2 * Math.PI;
    }
  }

  /// <summary>
  /// Initializes a new instance of <see cref="CRSoffsetRotation"/>.
  /// </summary>
  /// <param name="spatialReference">SpatialReference to apply offsets and rotation to.</param>
  public CRSoffsetRotation(ACG.SpatialReference spatialReference)
  {
    SpatialReference = spatialReference;
    LatOffset = 0;
    LonOffset = 0;
    TrueNorthRadians = 0;
  }

  /// <summary>
  /// Initializes a new instance of <see cref="CRSoffsetRotation"/>.
  /// </summary>
  /// <param name="map">Map to read metadata from.</param>
  public CRSoffsetRotation(Map map)
  {
    ACG.SpatialReference spatialReference = map.SpatialReference;

    SpatialReference = spatialReference;

    // read from metadata
    string metadata = map.GetMetadata();
    var root = XDocument.Parse(metadata).Root;
    string? textData = root?.Element("dataIdInfo")?.Element("resConst")?.Element("Consts")?.Element("useLimit")?.Value;
    textData = textData?.Replace("<SPAN>", "").Replace("</SPAN>", "");

    // set offsets and rotation from metadata if available
    // format to write to Metadata "Use Limitations" field:
    // _specklexoffset=100_speckleyoffset=200_specklenorth=0_

    if (
      textData != null
      && textData.Contains("_specklexoffset=", StringComparison.CurrentCultureIgnoreCase)
      && textData.Contains("_speckleyoffset=", StringComparison.CurrentCultureIgnoreCase)
      && textData.Contains("_specklenorth=", StringComparison.CurrentCultureIgnoreCase)
    )
    {
      string? latElement = textData.ToLower().Split("_speckleyoffset=")[^1].Split("_")[0];
      string? lonElement = textData.ToLower().Split("_specklexoffset=")[^1].Split("_")[0];
      string? northElement = textData.ToLower().Split("_specklenorth=")[^1].Split("_")[0];
      try
      {
        LatOffset = latElement is null ? 0 : Convert.ToDouble(latElement);
        LonOffset = lonElement is null ? 0 : Convert.ToDouble(lonElement);
        TrueNorthRadians = northElement is null ? 0 : Convert.ToDouble(northElement);
      }
      catch (Exception ex) when (ex is FormatException or OverflowException)
      {
        LatOffset = 0;
        LonOffset = 0;
        TrueNorthRadians = 0;
      }
    }
    else
    {
      LatOffset = 0;
      LonOffset = 0;
      TrueNorthRadians = 0;
    }
  }

  /// <summary>
  /// Initializes a new instance of <see cref="CRSoffsetRotation"/>.
  /// </summary>
  /// <param name="spatialReference">SpatialReference to apply offsets and rotation to.</param>
  /// <param name="latOffset">Latitude (Y) ofsset in the current SpatialReference units.</param>
  /// <param name="lonOffset">Longitude (X) ofsset in the current SpatialReference units.</param>
  /// <param name="trueNorthRadians">Angle to True North in radians.</param>
  public CRSoffsetRotation(
    ACG.SpatialReference spatialReference,
    double latOffset,
    double lonOffset,
    double trueNorthRadians
  )
  {
    SpatialReference = spatialReference;
    LatOffset = latOffset;
    LonOffset = lonOffset;
    TrueNorthRadians = trueNorthRadians;
  }
}
