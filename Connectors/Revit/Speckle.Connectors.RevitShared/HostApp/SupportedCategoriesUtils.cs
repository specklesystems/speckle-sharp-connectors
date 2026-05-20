using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

public static class SupportedCategoriesUtils
{
  /// <summary>
  /// Filters out all categories besides Model categories, and Grids in Annotation. This utility should be used
  /// to clean any elements we might want to send pre-conversion as well as in what categories
  /// to display in our category filter.
  /// </summary>
  /// <param name="category"></param>
  /// <returns></returns>
  public static bool IsSupportedCategory(Category? category)
  {
    if (category is null)
    {
      return false;
    }

    // stacked walls are "not visible in the ui" whereas they clearly are.
    // see [CNX-1301: Revit Stacked Walls are not sending](https://linear.app/speckle/issue/CNX-1301/revit-stacked-walls-are-not-sending)
    if (category.BuiltInCategory == BuiltInCategory.OST_StackedWalls)
    {
      return true;
    }

    if (!category.IsVisibleInUI) //&& category.BuiltInCategory != BuiltInCategory.OST_StackedWalls)
    {
      return false;
    }

    switch (category.CategoryType)
    {
      case CategoryType.Annotation:
        return category.BuiltInCategory == BuiltInCategory.OST_Grids;
      case CategoryType.AnalyticalModel:
        return true;

      case CategoryType.Model:
        return category.BuiltInCategory != BuiltInCategory.OST_AreaSchemes
          && category.BuiltInCategory != BuiltInCategory.OST_AreaSchemeLines;

      default:
        return false;
    }
  }
}
