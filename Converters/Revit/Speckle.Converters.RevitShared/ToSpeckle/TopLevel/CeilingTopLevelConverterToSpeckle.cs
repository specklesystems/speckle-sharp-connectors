using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects;
using Speckle.Objects.BuiltElements.Revit;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.Ceiling), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
internal sealed class CeilingTopLevelConverterToSpeckle : BaseTopLevelConverterToSpeckle<DB.Ceiling, SOBR.RevitCeiling>
{
  private readonly ITypedConverter<DB.CurveArrArray, List<SOG.Polycurve>> _curveArrArrayConverter;
  private readonly ITypedConverter<DB.Level, SOBR.RevitLevel> _levelConverter;
  private readonly ParameterValueExtractor _parameterValueExtractor;
  private readonly ParameterObjectAssigner _parameterObjectAssigner;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly IRevitConversionContextStack _contextStack;

  public CeilingTopLevelConverterToSpeckle(
    ITypedConverter<CurveArrArray, List<Polycurve>> curveArrArrayConverter,
    ITypedConverter<DB.Level, RevitLevel> levelConverter,
    ParameterValueExtractor parameterValueExtractor,
    ParameterObjectAssigner parameterObjectAssigner,
    DisplayValueExtractor displayValueExtractor,
    IRevitConversionContextStack contextStack
  )
  {
    _curveArrArrayConverter = curveArrArrayConverter;
    _levelConverter = levelConverter;
    _parameterValueExtractor = parameterValueExtractor;
    _parameterObjectAssigner = parameterObjectAssigner;
    _displayValueExtractor = displayValueExtractor;
    _contextStack = contextStack;
  }

  public override RevitCeiling Convert(DB.Ceiling target)
  {
    var elementType = (ElementType)target.Document.GetElement(target.GetTypeId());
    // POC: our existing receive operation is checking the "slopeDirection" prop,
    // but it is never being set. We should be setting it
    var level = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(target, DB.BuiltInParameter.LEVEL_PARAM);
    RevitLevel speckleLevel = _levelConverter.Convert(level);
    List<SOG.Mesh> displayValue = _displayValueExtractor.GetDisplayValue(target);

    RevitCeiling speckleCeiling =
      new()
      {
        type = elementType.Name,
        family = elementType.FamilyName,
        level = speckleLevel,
        displayValue = displayValue,
        units = _contextStack.Current.SpeckleUnits
      };

    var sketch = (Sketch)target.Document.GetElement(target.SketchId);
    List<SOG.Polycurve> profiles = _curveArrArrayConverter.Convert(sketch.Profile);
    // POC: https://spockle.atlassian.net/browse/CNX-9396
    if (profiles.Count > 0)
    {
      speckleCeiling.outline = profiles[0];
    }
    if (profiles.Count > 1)
    {
      speckleCeiling.voids = profiles.Skip(1).ToList<ICurve>();
    }

    _parameterObjectAssigner.AssignParametersToBase(target, speckleCeiling);

    return speckleCeiling;
  }
}
