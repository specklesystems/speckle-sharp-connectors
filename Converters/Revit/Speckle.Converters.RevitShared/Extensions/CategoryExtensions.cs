namespace Speckle.Converters.RevitShared.Extensions;

public static class CategoryExtensions
{
  public static DB.BuiltInCategory GetBuiltInCategory(this DB.Category category)
  {
#if REVIT2024_OR_GREATER
    return (DB.BuiltInCategory)category.Id.Value;
#else
    return (DB.BuiltInCategory)category.Id.IntegerValue;
#endif
  }
}
