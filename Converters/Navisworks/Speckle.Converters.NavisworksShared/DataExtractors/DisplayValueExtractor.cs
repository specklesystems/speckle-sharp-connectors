using Speckle.Sdk.Models;
using static Speckle.Converter.Navisworks.Helpers.ElementSelectionHelper;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class DisplayValueExtractor(GeometryToSpeckleConverter geometryConverter)
{
  private readonly GeometryToSpeckleConverter _geometryConverter =
    geometryConverter ?? throw new ArgumentNullException(nameof(geometryConverter));

  internal List<Base> GetDisplayValue(NAV.ModelItem modelItem) =>
    modelItem == null
      ? throw new ArgumentNullException(nameof(modelItem))
      : !modelItem.HasGeometry || !IsElementVisible(modelItem)
        ? []
        : _geometryConverter.Convert(modelItem);
}
