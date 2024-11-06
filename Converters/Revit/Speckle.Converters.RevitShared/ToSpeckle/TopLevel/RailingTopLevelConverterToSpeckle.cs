using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle;

namespace Speckle.Converters.Revit2023.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(DBA.Railing), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RailingTopLevelConverterToSpeckle : BaseTopLevelConverterToSpeckle<DBA.Railing, SOBR.RevitElement>
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public RailingTopLevelConverterToSpeckle(
    DisplayValueExtractor displayValueExtractor,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _converterSettings = converterSettings;
  }

  public override SOBR.RevitElement Convert(DBA.Railing target)
  {
    string family = target.Document.GetElement(target.GetTypeId()) is DB.FamilySymbol symbol
      ? symbol.FamilyName
      : "no family";
    string category = target.Category?.Name ?? "no category";
    var displayValue = _displayValueExtractor.GetDisplayValue(target);

    var topRail = _converterSettings.Current.Document.GetElement(target.TopRail);
    var topRailDisplayValue = _displayValueExtractor.GetDisplayValue(topRail);

    displayValue.AddRange(topRailDisplayValue);

    SOBR.RevitElement speckleElement =
      new()
      {
        type = target.Name,
        category = category,
        family = family,
        displayValue = displayValue
      };

    speckleElement["units"] = _converterSettings.Current.SpeckleUnits;

    return speckleElement;
  }
}
