using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.BuiltElements.Revit;

namespace Speckle.Converters.RevitShared.ToSpeckle;

// POC: used as placeholder for revit instances
[NameAndRankValue(nameof(DB.Element), 0)]
public class ElementTopLevelConverterToSpeckle : BaseTopLevelConverterToSpeckle<DB.Element, RevitElement>
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public ElementTopLevelConverterToSpeckle(
    DisplayValueExtractor displayValueExtractor,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _converterSettings = converterSettings;
  }

  public override RevitElement Convert(DB.Element target)
  {
    string family = target.Document.GetElement(target.GetTypeId()) is DB.FamilySymbol symbol
      ? symbol.FamilyName
      : "no family";
    string category = target.Category?.Name ?? "no category";
    List<Speckle.Objects.Geometry.Mesh> displayValue = _displayValueExtractor.GetDisplayValue(target);

    RevitElement speckleElement =
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
