using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Objects.BuiltElements.Revit;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

// POC: There is no validation on this converter to prevent conversion from "not a Revit Beam" to a Speckle Beam.
// This will definitely explode if we tried. Goes back to the `CanConvert` functionality conversation.
public class ColumnConversionToSpeckle : ITypedConverter<DB.FamilyInstance, RevitColumn>
{
  private readonly ITypedConverter<Location, Base> _locationConverter;
  private readonly ITypedConverter<Level, RevitLevel> _levelConverter;
  private readonly ParameterValueExtractor _parameterValueExtractor;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ParameterObjectAssigner _parameterObjectAssigner;

  public ColumnConversionToSpeckle(
    ITypedConverter<Location, Base> locationConverter,
    ITypedConverter<Level, RevitLevel> levelConverter,
    ParameterValueExtractor parameterValueExtractor,
    DisplayValueExtractor displayValueExtractor,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ParameterObjectAssigner parameterObjectAssigner
  )
  {
    _locationConverter = locationConverter;
    _levelConverter = levelConverter;
    _parameterValueExtractor = parameterValueExtractor;
    _displayValueExtractor = displayValueExtractor;
    _converterSettings = converterSettings;
    _parameterObjectAssigner = parameterObjectAssigner;
  }

  public RevitColumn Convert(DB.FamilyInstance target)
  {
    FamilySymbol symbol = (FamilySymbol)target.Document.GetElement(target.GetTypeId());
    List<SOG.Mesh> displayValue = _displayValueExtractor.GetDisplayValue(target);

    RevitColumn speckleColumn =
      new()
      {
        family = symbol.FamilyName,
        type = target.Document.GetElement(target.GetTypeId()).Name,
        facingFlipped = target.FacingFlipped,
        handFlipped = target.HandFlipped,
        isSlanted = target.IsSlantedColumn,
        displayValue = displayValue,
        units = _converterSettings.Current.SpeckleUnits
      };

    if (
      _parameterValueExtractor.TryGetValueAsDocumentObject<Level>(
        target,
        BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
        out var level
      )
    )
    {
      speckleColumn.level = _levelConverter.Convert(level);
    }
    if (
      _parameterValueExtractor.TryGetValueAsDocumentObject<Level>(
        target,
        BuiltInParameter.FAMILY_TOP_LEVEL_PARAM,
        out var topLevel
      )
    )
    {
      speckleColumn.topLevel = _levelConverter.Convert(topLevel);
    }

    if (
      _parameterValueExtractor.TryGetValueAsDouble(
        target,
        BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM,
        out var baseOffset
      )
    )
    {
      speckleColumn.baseOffset = baseOffset.NotNull();
    }

    if (
      _parameterValueExtractor.TryGetValueAsDouble(
        target,
        BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM,
        out var topOffset
      )
    )
    {
      speckleColumn.topOffset = topOffset.NotNull();
    }

    speckleColumn.baseLine =
      GetBaseCurve(target, speckleColumn.topLevel?.elevation ?? -1, speckleColumn.topOffset)
      ?? throw new SpeckleConversionException("Unable to find a valid baseCurve for column");

    if (target.Location is LocationPoint locationPoint)
    {
      speckleColumn.rotation = locationPoint.Rotation;
    }

    _parameterObjectAssigner.AssignParametersToBase(target, speckleColumn);

    return speckleColumn;
  }

  private ICurve? GetBaseCurve(DB.FamilyInstance target, double topLevelElevation, double topLevelOffset)
  {
    Base baseGeometry = _locationConverter.Convert(target.Location);
    ICurve? baseCurve = baseGeometry as ICurve;

    if (baseGeometry is ICurve)
    {
      return baseCurve;
    }
    else if (baseGeometry is SOG.Point basePoint)
    {
      // POC: in existing connector, we are sending column as Revit Instance instead of Column with the following if.
      // I am not sure why. I think this if is checking if the column has a fixed height
      //if (
      //  symbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased
      //  || symbol.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased
      //)
      //{
      //  return RevitInstanceToSpeckle(revitColumn, out notes, null);
      //}

      return new SOG.Line
      {
        start = basePoint,
        end = new SOG.Point(
          basePoint.x,
          basePoint.y,
          topLevelElevation + topLevelOffset,
          _converterSettings.Current.SpeckleUnits
        ),
        units = _converterSettings.Current.SpeckleUnits,
      };
    }

    return null;
  }
}
