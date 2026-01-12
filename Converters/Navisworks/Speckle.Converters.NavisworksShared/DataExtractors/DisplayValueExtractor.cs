using Speckle.Sdk.Models;
using static Speckle.Converter.Navisworks.Helpers.ElementSelectionHelper;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class DisplayValueExtractor(GeometryToSpeckleConverter geometryConverter)
{
  internal List<Base> GetDisplayValue(NAV.ModelItem modelItem) =>
    modelItem == null
      ? throw new ArgumentNullException(nameof(modelItem))
      : !modelItem.HasGeometry || !IsElementVisible(modelItem)
        ? []
        : GeometryConverter.Convert(modelItem);

  /// <summary>
  /// Gets the underlying geometry converter for accessing cache statistics.
  /// </summary>
  public GeometryToSpeckleConverter GeometryConverter { get; } =
    geometryConverter ?? throw new ArgumentNullException(nameof(geometryConverter));
}
