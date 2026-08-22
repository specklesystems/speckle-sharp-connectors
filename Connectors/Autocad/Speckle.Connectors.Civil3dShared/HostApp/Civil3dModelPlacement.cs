using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.Settings;
using Speckle.Converters.Autocad;

namespace Speckle.Connectors.Civil3dShared.HostApp;

internal static class Civil3dModelPlacement
{
  private const string GRID_COORDINATES = "gridCoordinates";

  public static AutocadConversionSettings Configure(
    AutocadConversionSettings settings,
    AutocadModelPlacement requestedPlacement
  )
  {
    var options = new Dictionary<string, Matrix3d>(StringComparer.Ordinal);
    if (settings.ModelPlacementOptions is not null)
    {
      foreach (var option in settings.ModelPlacementOptions)
      {
        options[option.Key] = option.Value;
      }
    }
    var metadata = new Dictionary<string, AutocadModelProperty>(StringComparer.Ordinal);

    CivilDocument civilDocument = CivilDocument.GetCivilDocument(settings.Document.Database);
    SettingsDrawing drawing = civilDocument.Settings.DrawingSettings;
    SettingsUnitZone unitZone = drawing.UnitZoneSettings;
    string? coordinateSystemCode = NormalizeCoordinateSystemCode(unitZone.CoordinateSystemCode);
    string coordinateUnits = unitZone.DrawingUnits == DrawingUnitType.Meters ? "m" : "ft";

    if (coordinateSystemCode is not null)
    {
      int? epsgCode = TryGetEpsgCode(coordinateSystemCode);
      bool hasExplicitEpsg = coordinateSystemCode.StartsWith("EPSG", StringComparison.OrdinalIgnoreCase);
      metadata["crs.horizontal.code"] = new(epsgCode is int epsg ? $"EPSG:{epsg}" : coordinateSystemCode);
      metadata["crs.horizontal.authority"] = new(epsgCode is not null || hasExplicitEpsg ? "EPSG" : "Autodesk");
      if (epsgCode is not null)
      {
        metadata["crs.horizontal.nativeCode"] = new(coordinateSystemCode);
      }
      metadata["crs.axisOrder"] = new("easting,northing");
      metadata["crs.units"] = new(coordinateUnits);
    }

    Matrix3d? localToGrid = null;
    if (coordinateSystemCode is not null && !drawing.ApplyTransformSettings)
    {
      // Civil 3D defines AutoCAD X/Y as grid easting/northing when a zone is assigned and drawing
      // transformation settings are disabled.
      localToGrid = Matrix3d.Identity;
      metadata["coordinateOperation.localToGrid.enabled"] = new(false);
      metadata["coordinateOperation.localToGrid.isAffine"] = new(true);
      metadata["coordinateOperation.localToGrid.method"] = new("identity");
    }
    else if (drawing.ApplyTransformSettings)
    {
      SettingsTransformation transform = drawing.TransformationSettings;
      bool isAffine = transform.GridScaleFactorComputation != GridScaleFactorType.PrismodialFormula;
      metadata["coordinateOperation.localToGrid.enabled"] = new(true);
      metadata["coordinateOperation.localToGrid.isAffine"] = new(isAffine);
      metadata["coordinateOperation.localToGrid.method"] = new(ToMethod(transform.GridScaleFactorComputation));
      metadata["coordinateOperation.localToGrid.rotationMethod"] = new(ToRotationMethod(transform.SpecifyRotationType));
      metadata["coordinateOperation.localToGrid.rotationToGridNorth"] = new(transform.RotationToGridNorth, "deg");
      metadata["coordinateOperation.localToGrid.gridScaleFactor"] = new(transform.GridScaleFactor);
      AddPointMetadata(
        metadata,
        "coordinateOperation.localToGrid.localReferencePoint",
        transform.LocalReferencePoint,
        settings.SpeckleUnits
      );
      AddPointMetadata(
        metadata,
        "coordinateOperation.localToGrid.gridReferencePoint",
        transform.GridReferencePoint,
        coordinateUnits
      );

      if (isAffine)
      {
        double scale = CombinedScaleFactor(transform);
        double rotation = RotationRadians(transform);
        localToGrid = CreateLocalToGridMatrix(
          transform.LocalReferencePoint,
          transform.GridReferencePoint,
          rotation,
          scale
        );
        metadata["coordinateOperation.localToGrid.combinedScaleFactor"] = new(scale);
      }
    }

    if (localToGrid is Matrix3d gridTransform)
    {
      options[GRID_COORDINATES] = gridTransform;
    }

    string source = requestedPlacement switch
    {
      AutocadModelPlacement.CurrentUcs => "currentUcs",
      AutocadModelPlacement.GridCoordinates when options.ContainsKey(GRID_COORDINATES) => GRID_COORDINATES,
      _ => "drawingWcs",
    };
    Matrix3d storedWcsToSource = options[source];

    return settings with
    {
      ReferencePointTransform =
        settings.ApplyTransform && storedWcsToSource != Matrix3d.Identity ? storedWcsToSource.Inverse() : null,
      ModelPlacementSource = source,
      ModelPlacementOptions = options,
      CoordinateMetadata = metadata,
    };
  }

  private static string? NormalizeCoordinateSystemCode(string? code)
  {
    if (string.IsNullOrWhiteSpace(code) || code == ".")
    {
      return null;
    }
    return code!.Trim();
  }

  private static int? TryGetEpsgCode(string coordinateSystemCode)
  {
    try
    {
      using GeoCoordinateSystem coordinateSystem = GeoCoordinateSystem.Create(coordinateSystemCode);
      return coordinateSystem.EPSGcode > 0 ? coordinateSystem.EPSGcode : null;
    }
    catch (Autodesk.AutoCAD.Runtime.Exception)
    {
      // Custom and non-earth Autodesk coordinate systems do not necessarily have an EPSG equivalent.
      return null;
    }
    catch (ArgumentException)
    {
      return null;
    }
  }

  private static string ToMethod(GridScaleFactorType method) =>
    method switch
    {
      GridScaleFactorType.Unity => "unity",
      GridScaleFactorType.UserDefined => "userDefined",
      GridScaleFactorType.ReferencePoint => "referencePoint",
      GridScaleFactorType.PrismodialFormula => "prismoidal",
      _ => method.ToString(),
    };

  private static string ToRotationMethod(SpecifyRotationType method) =>
    method == SpecifyRotationType.RotationPoint ? "referencePoint" : "gridNorthAngle";

  private static void AddPointMetadata(
    Dictionary<string, AutocadModelProperty> metadata,
    string prefix,
    Point2d point,
    string units
  )
  {
    metadata[$"{prefix}.x"] = new(point.X, units);
    metadata[$"{prefix}.y"] = new(point.Y, units);
  }

  private static double CombinedScaleFactor(SettingsTransformation transform)
  {
    double gridScale =
      transform.GridScaleFactorComputation == GridScaleFactorType.Unity ? 1.0 : transform.GridScaleFactor;
    if (!transform.ApplySeaLevelScaleFactor || transform.SpheroidRadius == 0)
    {
      return gridScale;
    }

    double seaLevelScale = transform.SpheroidRadius / (transform.SpheroidRadius + transform.SeaLevelScaleElevation);
    return gridScale * seaLevelScale;
  }

  private static double RotationRadians(SettingsTransformation transform)
  {
    if (transform.SpecifyRotationType == SpecifyRotationType.RotationPoint)
    {
      Vector2d local = transform.LocalRotationPoint - transform.LocalReferencePoint;
      Vector2d grid = transform.GridRotationPoint - transform.GridReferencePoint;
      if (local.Length > 1e-12 && grid.Length > 1e-12)
      {
        return Math.Atan2(grid.Y, grid.X) - Math.Atan2(local.Y, local.X);
      }
    }

    // Civil survey angles are positive clockwise from north; AutoCAD matrix rotations are positive
    // counter-clockwise from +X.
    return -transform.RotationToGridNorth * Math.PI / 180.0;
  }

  private static Matrix3d CreateLocalToGridMatrix(
    Point2d localReference,
    Point2d gridReference,
    double rotation,
    double scale
  )
  {
    double cos = Math.Cos(rotation);
    double sin = Math.Sin(rotation);
    double a = scale * cos;
    double b = scale * sin;
    double translationX = gridReference.X - (a * localReference.X - b * localReference.Y);
    double translationY = gridReference.Y - (b * localReference.X + a * localReference.Y);

    return new Matrix3d(new[] { a, -b, 0, translationX, b, a, 0, translationY, 0, 0, 1, 0, 0, 0, 0, 1 });
  }
}
