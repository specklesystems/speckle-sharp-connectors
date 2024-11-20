using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.BuiltElements.Revit;

namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

public class ClassPropertiesExtractor
{
  private readonly ParameterValueExtractor _parameterValueExtractor;
  private readonly ITypedConverter<DB.CurveArrArray, List<SOG.Polycurve>> _curveArrArrayConverter;
  private readonly ITypedConverter<DB.ModelCurveArray, SOG.Polycurve> _modelCurveArrayConverter;
  private readonly ITypedConverter<DB.ModelCurveArrArray, SOG.Polycurve[]> _modelCurveArrArrayConverter;
  private readonly ITypedConverter<IList<DB.BoundarySegment>, SOG.Polycurve> _boundarySegmentConverter;

  // POC: for now, we are still converting and attaching levels to every single object
  // This should probably be changed to level proxies
  private readonly ITypedConverter<DB.Level, RevitLevel> _levelConverter;

  public ClassPropertiesExtractor(
    ParameterValueExtractor parameterValueExtractor,
    ITypedConverter<DB.CurveArrArray, List<SOG.Polycurve>> curveArrArrayConverter,
    ITypedConverter<DB.ModelCurveArray, SOG.Polycurve> modelCurveArrayConverter,
    ITypedConverter<DB.ModelCurveArrArray, SOG.Polycurve[]> modelCurveArrArrayConverter,
    ITypedConverter<IList<DB.BoundarySegment>, SOG.Polycurve> boundarySegmentConverter,
    ITypedConverter<DB.Level, RevitLevel> levelConverter
  )
  {
    _parameterValueExtractor = parameterValueExtractor;
    _curveArrArrayConverter = curveArrArrayConverter;
    _modelCurveArrayConverter = modelCurveArrayConverter;
    _modelCurveArrArrayConverter = modelCurveArrArrayConverter;
    _boundarySegmentConverter = boundarySegmentConverter;
    _levelConverter = levelConverter;
  }

  public Dictionary<string, object?>? GetClassProperties(DB.Element element)
  {
    switch (element)
    {
      case DB.Wall wall:
        return ExtractWallProperties(wall);

      case DB.Floor floor:
        return ExtractFloorProperties(floor);

      case DB.Ceiling ceiling:
        return ExtractCeilingProperties(ceiling);

      case DB.ExtrusionRoof extrusionRoof:
        return ExtractExtrusionRoofProperties(extrusionRoof);

      case DB.FootPrintRoof footPrintRoof:
        return ExtractFootPrintRoofProperties(footPrintRoof);

      case DB.FamilyInstance familyInstance:
        return ExtractFamilyInstanceProperties(familyInstance);

      case DBA.Room room:
        return ExtractRoomProperties(room);

      default:
        return null;
    }
  }

  private Dictionary<string, object?> ExtractWallProperties(DB.Wall wall)
  {
    var level = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(
      wall,
      DB.BuiltInParameter.WALL_BASE_CONSTRAINT
    );

    var topLevel = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(
      wall,
      DB.BuiltInParameter.WALL_BASE_CONSTRAINT
    );

    Dictionary<string, object?> wallProperties =
      new()
      {
        ["@level"] = _levelConverter.Convert(level),
        ["@topLevel"] = _levelConverter.Convert(topLevel),
        ["isStructural"] =
          _parameterValueExtractor.GetValueAsBool(wall, DB.BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT) ?? false,
        ["flipped"] = wall.Flipped
      };

    // get profile curves, which includes voids
    List<SOG.Polycurve> profile = GetSketchProfile(wall.Document, wall.SketchId);
    if (profile.Count > 0)
    {
      wallProperties["profile"] = profile;
    }

    return wallProperties;
  }

  private Dictionary<string, object?> ExtractFloorProperties(DB.Floor floor)
  {
    var level = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(floor, DB.BuiltInParameter.LEVEL_PARAM);

    Dictionary<string, object?> floorProperties =
      new()
      {
        ["@level"] = _levelConverter.Convert(level),
        ["isStructural"] =
          _parameterValueExtractor.GetValueAsBool(floor, DB.BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL) ?? false,
      };

    // get profile curves, which includes voids
    List<SOG.Polycurve> profile = GetSketchProfile(floor.Document, floor.SketchId);
    if (profile.Count > 0)
    {
      floorProperties["profile"] = profile;
    }

    return floorProperties;
  }

  private Dictionary<string, object?> ExtractCeilingProperties(DB.Ceiling ceiling)
  {
    var level = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(ceiling, DB.BuiltInParameter.LEVEL_PARAM);

    Dictionary<string, object?> ceilingProperties = new() { ["@level"] = _levelConverter.Convert(level) };

    // get profile curves, which includes voids
    List<SOG.Polycurve> profile = GetSketchProfile(ceiling.Document, ceiling.SketchId);
    if (profile.Count > 0)
    {
      ceilingProperties["profile"] = profile;
    }

    return ceilingProperties;
  }

  private Dictionary<string, object?> ExtractExtrusionRoofProperties(DB.ExtrusionRoof extrusionRoof)
  {
    var level = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(
      extrusionRoof,
      DB.BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM
    );

    // get profile curve, which is outline
    SOG.Polycurve profile = _modelCurveArrayConverter.Convert(extrusionRoof.GetProfile());

    Dictionary<string, object?> extrusionRoofProperties =
      new()
      {
        ["@level"] = _levelConverter.Convert(level),
        ["start"] = _parameterValueExtractor.GetValueAsDouble(extrusionRoof, DB.BuiltInParameter.EXTRUSION_START_PARAM),
        ["end"] = _parameterValueExtractor.GetValueAsDouble(extrusionRoof, DB.BuiltInParameter.EXTRUSION_END_PARAM),
        ["profile"] = new List<SOG.Polycurve>() { profile }
      };

    return extrusionRoofProperties;
  }

  private Dictionary<string, object?> ExtractFootPrintRoofProperties(DB.FootPrintRoof footPrintRoof)
  {
    var level = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(
      footPrintRoof,
      DB.BuiltInParameter.ROOF_BASE_LEVEL_PARAM
    );

    // get profile curve, which is outline
    SOG.Polycurve[] profile = _modelCurveArrArrayConverter.Convert(footPrintRoof.GetProfiles());

    Dictionary<string, object?> extrusionRoofProperties =
      new() { ["@level"] = _levelConverter.Convert(level), ["profile"] = profile.ToList() };

    // We don't currently validate the success of this TryGet, it is assumed some Roofs don't have a top-level.
    if (
      _parameterValueExtractor.TryGetValueAsDocumentObject<DB.Level>(
        footPrintRoof,
        DB.BuiltInParameter.ROOF_UPTO_LEVEL_PARAM,
        out var topLevel
      )
    )
    {
      extrusionRoofProperties["@topLevel"] = _levelConverter.Convert(topLevel);
    }

    return extrusionRoofProperties;
  }

  private Dictionary<string, object?> ExtractFamilyInstanceProperties(DB.FamilyInstance familyInstance)
  {
    Dictionary<string, object?> familyInstanceProperties =
      new() { ["type"] = familyInstance.Document.GetElement(familyInstance.GetTypeId()).Name };

    if (
      _parameterValueExtractor.TryGetValueAsDocumentObject<DB.Level>(
        familyInstance,
        DB.BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
        out DB.Level? level
      )
    )
    {
      familyInstanceProperties["@level"] = _levelConverter.Convert(level);
    }

    if (
      _parameterValueExtractor.TryGetValueAsDocumentObject<DB.Level>(
        familyInstance,
        DB.BuiltInParameter.FAMILY_TOP_LEVEL_PARAM,
        out DB.Level? topLevel
      )
    )
    {
      familyInstanceProperties["@topLevel"] = _levelConverter.Convert(topLevel);
    }

    if (familyInstance.StructuralType == DB.Structure.StructuralType.Column)
    {
      familyInstanceProperties["facingFlipped"] = familyInstance.FacingFlipped;
      familyInstanceProperties["handFlipped"] = familyInstance.HandFlipped;
      familyInstanceProperties["IsSlantedColumn"] = familyInstance.IsSlantedColumn;
    }

    return familyInstanceProperties;
  }

  private Dictionary<string, object?> ExtractRoomProperties(DBA.Room room)
  {
    // get profile curve
    var profiles = room.GetBoundarySegments(new DB.SpatialElementBoundaryOptions())
      .Select(c => (Speckle.Objects.ICurve)_boundarySegmentConverter.Convert(c))
      .ToList();

    Dictionary<string, object?> roomProperties =
      new()
      {
        ["number"] = room.Number,
        ["roomName"] = _parameterValueExtractor.GetValueAsString(room, DB.BuiltInParameter.ROOM_NAME) ?? "-",
        ["area"] = _parameterValueExtractor.GetValueAsDouble(room, DB.BuiltInParameter.ROOM_AREA),
        ["@level"] = _levelConverter.Convert(room.Level),
        ["profile"] = profiles
      };

    return roomProperties;
  }

  // gets all sketch profile curves
  // we were assuming that for walls the first curve is the element and the rest of the curves are openings. this isn't always true.
  // https://spockle.atlassian.net/browse/CNX-9396
  // This is why we're now sending all sketch profile curves under "profile" property instead of splitting into outline and void curves
  private List<SOG.Polycurve> GetSketchProfile(DB.Document doc, DB.ElementId sketchId)
  {
    if (doc.GetElement(sketchId) is DB.Sketch sketch)
    {
      if (sketch.Profile is DB.CurveArrArray profile)
      {
        return _curveArrArrayConverter.Convert(profile);
      }
    }

    return new();
  }
}
