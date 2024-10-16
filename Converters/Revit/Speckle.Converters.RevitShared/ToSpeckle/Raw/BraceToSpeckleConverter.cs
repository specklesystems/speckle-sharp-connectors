using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

// POC: There is no validation on this converter to prevent conversion from "not a Revit Beam" to a Speckle Beam.
// This will definitely explode if we tried. Goes back to the `CanConvert` functionality conversation.
// As-is, what we are saying is that it can take "any Family Instance" and turn it into a Speckle.RevitBeam, which is far from correct.
// CNX-9312
public class BraceToSpeckleConverter : ITypedConverter<DB.FamilyInstance, SOBR.RevitBrace>
{
  private readonly ITypedConverter<DB.FamilyInstance, SOBR.RevitBeam> _beamConverter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public BraceToSpeckleConverter(
    ITypedConverter<DB.FamilyInstance, SOBR.RevitBeam> beamConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _beamConverter = beamConverter;
    _converterSettings = converterSettings;
  }

  public SOBR.RevitBrace Convert(DB.FamilyInstance target)
  {
    // POC: we might want some easy one-liner here to FamilyMatchesOrThrow(target, DB.Structure.StructuralType.Brace) or similar
    // and added in each Convert
    // POC: this and the beam lost the notes we were returning, though this seems against even the original pattern

    var beam = _beamConverter.Convert(target);

    var brace = new SOBR.RevitBrace()
    {
      applicationId = beam.applicationId,
      type = beam.type,
      baseLine = beam.baseLine,
      level = beam.level,
      family = beam.family,
      parameters = beam.parameters,
      displayValue = beam.displayValue,
      units = _converterSettings.Current.SpeckleUnits
    };

    var dynamicProps = beam.GetMembers(DynamicBaseMemberType.Dynamic);

    foreach (var dp in dynamicProps)
    {
      brace[dp.Key] = dp.Value;
    }

    return brace;
  }
}
