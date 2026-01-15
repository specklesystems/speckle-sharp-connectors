using Speckle.Converter.Navisworks.Services;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class DisplayValueExtractor(
  GeometryToSpeckleConverter geometryConverter,
  IElementSelectionService elementSelectionService
)
{
  internal List<Base> GetDisplayValue(NAV.ModelItem modelItem) =>
    modelItem == null
      ? throw new ArgumentNullException(nameof(modelItem))
      : !modelItem.HasGeometry || !elementSelectionService.IsVisible(modelItem)
        ? []
        : GeometryConverter.Convert(modelItem);

  /// <summary>
  /// Gets the underlying geometry converter for accessing cache statistics.
  /// </summary>
  internal GeometryToSpeckleConverter GeometryConverter { get; } =
    geometryConverter ?? throw new ArgumentNullException(nameof(geometryConverter));
}
