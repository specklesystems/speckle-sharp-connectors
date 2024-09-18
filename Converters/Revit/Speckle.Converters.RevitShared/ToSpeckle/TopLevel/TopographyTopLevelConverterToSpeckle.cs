using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.ToSpeckle;

// POC: needs review feels, BIG, feels like it could be broken down..
// i.e. GetParams(), GetGeom()? feels like it's doing too much
[NameAndRankValue(nameof(DBA.TopographySurface), 0)]
public class TopographyTopLevelConverterToSpeckle
  : BaseTopLevelConverterToSpeckle<DBA.TopographySurface, SOBR.RevitTopography>
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ParameterObjectAssigner _parameterObjectAssigner;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public TopographyTopLevelConverterToSpeckle(
    DisplayValueExtractor displayValueExtractor,
    ParameterObjectAssigner parameterObjectAssigner,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _parameterObjectAssigner = parameterObjectAssigner;
    _converterSettings = converterSettings;
  }

  public override SOBR.RevitTopography Convert(DBA.TopographySurface target)
  {
    var speckleTopo = new SOBR.RevitTopography
    {
      units = _converterSettings.Current.SpeckleUnits,
      displayValue = _displayValueExtractor.GetDisplayValue(target),
      elementId = target.Id.ToString().NotNull(),
      baseGeometry = null! //TODO: this can't be correct, see https://linear.app/speckle/issue/CNX-461/revit-check-why-topographytospeckle-sets-no-basegeometry
    };

    // POC: shouldn't we just do this in the RevitConverter ?
    _parameterObjectAssigner.AssignParametersToBase(target, speckleTopo);

    return speckleTopo;
  }
}
