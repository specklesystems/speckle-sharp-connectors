using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

// POC: There is no validation on this converter to prevent conversion from "not a Revit Beam" to a Speckle Beam.
// This will definitely explode if we tried. Goes back to the `CanConvert` functionality conversation.
// As-is, what we are saying is that it can take "any Family Instance" and turn it into a Speckle.RevitBeam, which is far from correct.
// CNX-9312
public class BeamConversionToSpeckle : ITypedConverter<DB.FamilyInstance, SOBR.RevitBeam>
{
  private readonly ITypedConverter<DB.Location, Base> _locationConverter;
  private readonly ITypedConverter<DB.Level, SOBR.RevitLevel> _levelConverter;
  private readonly ParameterValueExtractor _parameterValueExtractor;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public BeamConversionToSpeckle(
    ITypedConverter<DB.Location, Base> locationConverter,
    ITypedConverter<DB.Level, SOBR.RevitLevel> levelConverter,
    ParameterValueExtractor parameterValueExtractor,
    DisplayValueExtractor displayValueExtractor,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _locationConverter = locationConverter;
    _levelConverter = levelConverter;
    _parameterValueExtractor = parameterValueExtractor;
    _displayValueExtractor = displayValueExtractor;
    _converterSettings = converterSettings;
  }

  public SOBR.RevitBeam Convert(DB.FamilyInstance target)
  {
    var baseGeometry = _locationConverter.Convert(target.Location);
    if (baseGeometry is not ICurve baseCurve)
    {
      throw new ValidationException(
        $"Beam location conversion did not yield an ICurve, instead it yielded an object of type {baseGeometry.GetType()}"
      );
    }
    var symbol = (DB.FamilySymbol)target.Document.GetElement(target.GetTypeId());
    var level = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(
      target,
      DB.BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM
    );
    List<SOG.Mesh> displayValue = _displayValueExtractor.GetDisplayValue(target);

    SOBR.RevitBeam speckleBeam =
      new()
      {
        family = symbol.FamilyName,
        type = target.Document.GetElement(target.GetTypeId()).Name,
        baseLine = baseCurve,
        level = _levelConverter.Convert(level),
        displayValue = displayValue,
        units = _converterSettings.Current.SpeckleUnits
      };

    return speckleBeam;
  }
}
