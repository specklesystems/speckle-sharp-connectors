using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

public static class SupportedCategoriesUtils
{
  /// <summary>
  /// Filters out all categories besides Model categories. This utility should be used
  /// to clean any elements we might want to send pre-conversion as well as in what categories
  /// to display in our category filter.
  /// </summary>
  /// <param name="category"></param>
  /// <returns></returns>
  public static bool IsSupportedCategory(Category category)
  {
    return (
        category.CategoryType == CategoryType.Model
      // || category.CategoryType == CategoryType.AnalyticalModel
      )
#if REVIT_2023_OR_GREATER
      && category.BuiltInCategory != BuiltInCategory.OST_AreaSchemes
      && category.BuiltInCategory != BuiltInCategory.OST_AreaSchemeLines
#else
      && category.Name != "OST_AreaSchemeLines"
      && category.Name != "OST_AreaSchemes"
#endif
      && category.IsVisibleInUI;
  }
}
