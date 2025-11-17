using Speckle.Sdk.Models;
using static Speckle.Converter.Navisworks.Helpers.ElementSelectionHelper;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class DisplayValueExtractor(GeometryToSpeckleConverter geometryConverter)
{
  internal List<Base> GetDisplayValue(NAV.ModelItem modelItem) =>
    modelItem == null
      ? throw new ArgumentNullException(nameof(modelItem))
      : !modelItem.HasGeometry
        ? ([])
        : !IsElementVisible(modelItem)
          ? []
          :
          // this can be meshes or the instance reference objects
          // the un transformed objects stored in a separate collection
          geometryConverter.Convert(modelItem);
}
