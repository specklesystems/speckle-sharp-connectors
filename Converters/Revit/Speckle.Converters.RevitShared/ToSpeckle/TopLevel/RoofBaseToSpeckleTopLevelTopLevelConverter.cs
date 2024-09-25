using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.BuiltElements.Revit.RevitRoof;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(RoofBase), 0)]
internal sealed class RoofBaseToSpeckleTopLevelTopLevelConverter
  : BaseTopLevelConverterToSpeckle<DB.RoofBase, RevitRoof>
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public RoofBaseToSpeckleTopLevelTopLevelConverter(
    DisplayValueExtractor displayValueExtractor,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _converterSettings = converterSettings;
  }

  public override RevitRoof Convert(RoofBase target)
  {
    var elementType = (ElementType)target.Document.GetElement(target.GetTypeId());
    List<Speckle.Objects.Geometry.Mesh> displayValue = _displayValueExtractor.GetDisplayValue(target);

    RevitRoof revitRoof =
      new()
      {
        type = elementType.Name,
        family = elementType.FamilyName,
        displayValue = displayValue,
        units = _converterSettings.Current.SpeckleUnits
      };
    return revitRoof;
  }
}
