using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.BuiltElements.Revit;
using Speckle.Objects.BuiltElements.Revit.RevitRoof;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.ExtrusionRoof), 0)]
public class ExtrusionRoofToSpeckleTopLevelConverter
  : BaseTopLevelConverterToSpeckle<DB.ExtrusionRoof, RevitExtrusionRoof>
{
  private readonly ITypedConverter<DB.Level, SOBR.RevitLevel> _levelConverter;
  private readonly ITypedConverter<DB.ModelCurveArray, SOG.Polycurve> _modelCurveArrayConverter;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _pointConverter;
  private readonly ParameterValueExtractor _parameterValueExtractor;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ParameterObjectAssigner _parameterObjectAssigner;
  private readonly IRevitConversionContextStack _contextStack;

  public ExtrusionRoofToSpeckleTopLevelConverter(
    ITypedConverter<DB.Level, SOBR.RevitLevel> levelConverter,
    ITypedConverter<DB.ModelCurveArray, SOG.Polycurve> modelCurveArrayConverter,
    ITypedConverter<DB.XYZ, SOG.Point> pointConverter,
    ParameterValueExtractor parameterValueExtractor,
    DisplayValueExtractor displayValueExtractor,
    ParameterObjectAssigner parameterObjectAssigner,
    IRevitConversionContextStack contextStack
  )
  {
    _levelConverter = levelConverter;
    _modelCurveArrayConverter = modelCurveArrayConverter;
    _pointConverter = pointConverter;
    _parameterValueExtractor = parameterValueExtractor;
    _displayValueExtractor = displayValueExtractor;
    _parameterObjectAssigner = parameterObjectAssigner;
    _contextStack = contextStack;
  }

  public override RevitExtrusionRoof Convert(DB.ExtrusionRoof target)
  {
    SOG.Line referenceLine = ConvertReferenceLine(target);
    var level = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(
      target,
      DB.BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM
    );
    RevitLevel speckleLevel = _levelConverter.Convert(level);
    SOG.Polycurve outline = _modelCurveArrayConverter.Convert(target.GetProfile());
    var elementType = (DB.ElementType)target.Document.GetElement(target.GetTypeId());
    List<SOG.Mesh> displayValue = _displayValueExtractor.GetDisplayValue(target);

    RevitExtrusionRoof speckleExtrusionRoof =
      new()
      {
        start = _parameterValueExtractor.GetValueAsDouble(target, DB.BuiltInParameter.EXTRUSION_START_PARAM),
        end = _parameterValueExtractor.GetValueAsDouble(target, DB.BuiltInParameter.EXTRUSION_END_PARAM),
        type = elementType.Name,
        family = elementType.FamilyName,
        outline = outline,
        referenceLine = referenceLine,
        level = speckleLevel,
        displayValue = displayValue,
        units = _contextStack.Current.SpeckleUnits
      };

    _parameterObjectAssigner.AssignParametersToBase(target, speckleExtrusionRoof);

    return speckleExtrusionRoof;
  }

  private SOG.Line ConvertReferenceLine(DB.ExtrusionRoof target)
  {
    var plane = target.GetProfile().get_Item(0).SketchPlane.GetPlane();
    SOG.Line referenceLine =
      new()
      {
        start = _pointConverter.Convert(plane.Origin.Add(plane.XVec.Normalize().Negate())),
        end = _pointConverter.Convert(plane.Origin),
        units = _contextStack.Current.SpeckleUnits,
      };
    return referenceLine;
  }
}
