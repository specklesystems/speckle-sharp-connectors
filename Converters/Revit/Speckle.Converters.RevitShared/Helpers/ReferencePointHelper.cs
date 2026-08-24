using System.Globalization;
using Autodesk.Revit.DB;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Helper class for working with transform data coming from reference point setting
/// This allows preserving the reference point information between operations.
/// </summary>
public static class ReferencePointHelper
{
  public static RevitModelPlacementData GetModelPlacementData(Document document)
  {
    using var collector = new FilteredElementCollector(document);
    var basePoints = collector.OfClass(typeof(BasePoint)).Cast<BasePoint>().ToList();
    BasePoint? projectBasePoint = basePoints.FirstOrDefault(point => !point.IsShared);
    BasePoint? surveyPoint = basePoints.FirstOrDefault(point => point.IsShared);

    var sourceTransforms = new Dictionary<string, Transform> { ["internalOrigin"] = Transform.Identity };
    if (projectBasePoint is not null)
    {
      sourceTransforms["projectBasePoint"] = Transform.CreateTranslation(projectBasePoint.Position);
    }
    if (surveyPoint is not null)
    {
      sourceTransforms["surveyPoint"] = Transform.CreateTranslation(surveyPoint.Position);
    }
    if (document.ActiveProjectLocation?.GetTotalTransform() is { } sharedCoordinatesTransform)
    {
      sourceTransforms["sharedCoordinates"] = sharedCoordinatesTransform;
    }

    SiteLocation siteLocation = document.SiteLocation;
    string nativeCrsCode = siteLocation.GeoCoordinateSystemId?.Trim() ?? string.Empty;
    (string crsAuthority, string crsCode) = NormalizeRevitCrsCode(nativeCrsCode);

    return new RevitModelPlacementData(
      sourceTransforms,
      projectBasePoint?.Position,
      surveyPoint?.Position,
      surveyPoint?.SharedPosition,
      siteLocation.Latitude * (180d / Math.PI),
      siteLocation.Longitude * (180d / Math.PI),
      siteLocation.Elevation,
      crsAuthority,
      crsCode,
      nativeCrsCode,
      siteLocation.GeoCoordinateSystemDefinition?.Trim() ?? string.Empty
    );
  }

  private static (string Authority, string Code) NormalizeRevitCrsCode(string nativeCode)
  {
    const string EPSG_PREFIX = "EPSG:";
    const string ADSK_PREFIX = "ADSK:";
    if (nativeCode.StartsWith(EPSG_PREFIX, StringComparison.OrdinalIgnoreCase))
    {
      return ("EPSG", $"EPSG:{nativeCode[EPSG_PREFIX.Length..]}");
    }
    if (int.TryParse(nativeCode, out int epsg))
    {
      return ("EPSG", $"EPSG:{epsg}");
    }
    if (nativeCode.StartsWith(ADSK_PREFIX, StringComparison.OrdinalIgnoreCase))
    {
      return ("Autodesk", nativeCode[ADSK_PREFIX.Length..]);
    }
    return nativeCode.Length == 0 ? (string.Empty, string.Empty) : ("Autodesk", nativeCode);
  }

  /// <summary>
  /// Flattens a Revit <see cref="Transform"/> to the 16-element matrix Speckle stores (basis vectors in the first
  /// three "columns", translation in the last), used by <see cref="CreateTransformDataForRootObject"/> — the same
  /// element order <see cref="GetTransformFromRootObject"/> reads back.
  /// </summary>
  private static double[] TransformToArray(Transform transform) =>
    new[]
    {
      transform.BasisX.X,
      transform.BasisX.Y,
      transform.BasisX.Z,
      0,
      transform.BasisY.X,
      transform.BasisY.Y,
      transform.BasisY.Z,
      0,
      transform.BasisZ.X,
      transform.BasisZ.Y,
      transform.BasisZ.Z,
      0,
      transform.Origin.X,
      transform.Origin.Y,
      transform.Origin.Z,
      1,
    };

  /// <summary>
  /// Changes Revit Transform to a double array.
  /// Uses a 16-element column-major matrix representation. See https://speckle.guide/dev/objects.html
  /// </summary>
  public static Dictionary<string, object> CreateTransformDataForRootObject(Transform transform) =>
    new()
    {
      // TODO: it would also be nice to include the key-value pair for reference point type as a string
      { "transform", TransformToArray(transform) },
    };

  /// <summary>
  /// Combines the receiver's local reference-point setting with the transform the sender baked into the geometry,
  /// so the received model lands where the receive setting asks. Mirrors the v1 receive-side composition: with no
  /// local setting (receive = Source) the sender's transform is applied as-is (restoring the source's internal
  /// coordinates); with no sender transform the local setting stands alone; otherwise they compose.
  /// </summary>
  public static Transform? CalculateNewTransform(Transform? receiveTransform, Transform? rootTransform)
  {
    if (receiveTransform == null)
    {
      return rootTransform;
    }

    if (rootTransform == null)
    {
      return receiveTransform;
    }

    return rootTransform.Multiply(receiveTransform);
  }

  public static Matrix4x4 TransformToMatrix(Transform transform) =>
    new()
    {
      M11 = transform.BasisX.X,
      M21 = transform.BasisX.Y,
      M31 = transform.BasisX.Z,
      M41 = 0,

      M12 = transform.BasisY.X,
      M22 = transform.BasisY.Y,
      M32 = transform.BasisY.Z,
      M42 = 0,

      M13 = transform.BasisZ.X,
      M23 = transform.BasisZ.Y,
      M33 = transform.BasisZ.Z,
      M43 = 0,

      M14 = transform.Origin.X,
      M24 = transform.Origin.Y,
      M34 = transform.Origin.Z,
      M44 = 1,
    };

  public static string TransformToCsv(Transform transform)
  {
    Matrix4x4 m = TransformToMatrix(transform);
    return string.Join(
      ",",
      new[]
      {
        m.M11,
        m.M12,
        m.M13,
        m.M14,
        m.M21,
        m.M22,
        m.M23,
        m.M24,
        m.M31,
        m.M32,
        m.M33,
        m.M34,
        m.M41,
        m.M42,
        m.M43,
        m.M44,
        // "R" (round-trip) so parsing recovers the exact double on net48 too (plain ToString caps at 15 significant
        // digits there); matches the AutoCAD writer of the same modelPlacement.*.transform field family.
      }.Select(value => value.ToString("R", CultureInfo.InvariantCulture))
    );
  }

  /// <summary>
  /// Extracts and reconstructs a transform from the matrix data stored on root object
  /// </summary>
  public static Transform? GetTransformFromRootObject(object? matrixDataObj)
  {
    double[]? matrixData = null;

    // NOTE: why all these if checks? We send double[] but get List<object> back on receive, so need to convert
    if (matrixDataObj is double[] doubleArray)
    {
      matrixData = doubleArray;
    }
    else if (matrixDataObj is List<object> listValues)
    {
      matrixData = listValues.Select(v => Convert.ToDouble(v)).ToArray();
    }

    if (matrixData == null || matrixData.Length != 16)
    {
      return null;
    }

    // Extract components from the matrix
    XYZ basisX = new(matrixData[0], matrixData[1], matrixData[2]);
    XYZ basisY = new(matrixData[4], matrixData[5], matrixData[6]);
    XYZ basisZ = new(matrixData[8], matrixData[9], matrixData[10]);
    XYZ origin = new(matrixData[12], matrixData[13], matrixData[14]);

    Transform transform = Transform.Identity;
    transform.Origin = origin;
    transform.BasisX = basisX;
    transform.BasisY = basisY;
    transform.BasisZ = basisZ;

    return transform;
  }
}

public sealed class RevitModelPlacementData(
  Dictionary<string, Transform> sourceTransforms,
  XYZ? projectBasePointPosition,
  XYZ? surveyPointPosition,
  XYZ? surveyPointSharedPosition,
  double siteLatitudeDegrees,
  double siteLongitudeDegrees,
  double siteElevation,
  string crsAuthority,
  string crsCode,
  string crsNativeCode,
  string crsDefinition
)
{
  public XYZ? ProjectBasePointPosition { get; } = projectBasePointPosition;
  public XYZ? SurveyPointPosition { get; } = surveyPointPosition;
  public XYZ? SurveyPointSharedPosition { get; } = surveyPointSharedPosition;
  public double SiteLatitudeDegrees { get; } = siteLatitudeDegrees;
  public double SiteLongitudeDegrees { get; } = siteLongitudeDegrees;
  public double SiteElevation { get; } = siteElevation;
  public string CrsAuthority { get; } = crsAuthority;
  public string CrsCode { get; } = crsCode;
  public string CrsNativeCode { get; } = crsNativeCode;
  public string CrsDefinition { get; } = crsDefinition;

  public void SetSourceTransform(string kind, Transform transform) => sourceTransforms[kind] = transform;

  public Transform? GetStoredToOptionTransform(string kind, bool appliedToGeometry, Transform? selectedSourceTransform)
  {
    if (!sourceTransforms.TryGetValue(kind, out Transform? sourceTransform))
    {
      return null;
    }

    Transform storedToInternal =
      appliedToGeometry && selectedSourceTransform is not null ? selectedSourceTransform : Transform.Identity;
    return sourceTransform.Inverse.Multiply(storedToInternal);
  }
}
