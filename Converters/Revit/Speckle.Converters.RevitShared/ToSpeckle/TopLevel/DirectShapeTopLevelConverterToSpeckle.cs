using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Revit2023.ToSpeckle;

[NameAndRankValue(nameof(DB.DirectShape), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DirectShapeTopLevelConverterToSpeckle : BaseTopLevelConverterToSpeckle<DB.DirectShape, SOBR.DirectShape>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly DisplayValueExtractor _displayValueExtractor;

  public DirectShapeTopLevelConverterToSpeckle(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    DisplayValueExtractor displayValueExtractor
  )
  {
    _converterSettings = converterSettings;
    _displayValueExtractor = displayValueExtractor;
  }

  public override SOBR.DirectShape Convert(DB.DirectShape target)
  {
    var category = target.Category.GetBuiltInCategory().GetSchemaBuilderCategoryFromBuiltIn();

    // POC: Making the analogy that the DisplayValue is the same as the Geometries is only valid while we don't support Solids on send.
    var geometries = _displayValueExtractor.GetDisplayValue(target).Cast<Base>().ToList();

    SOBR.DirectShape result =
      new()
      {
        name = target.Name,
        category = category,
        baseGeometries = geometries,
        displayValue = geometries,
        units = _converterSettings.Current.SpeckleUnits,
        elementId = target.Id.ToString().NotNull()
      };

    result["type"] = target.Name;

    return result;
  }
}
