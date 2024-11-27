using Autodesk.Revit.DB;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.RevitShared.Extensions;

public static class CategoryExtensions
{
  public static SOBR.RevitCategory GetSchemaBuilderCategoryFromBuiltIn(this DB.BuiltInCategory builtInCategory)
  {
    // Clean up built-in name "OST_Walls" to be just "WALLS"
    var cleanName = builtInCategory
      .ToString()
      .Replace("OST_IOS", "") //for OST_IOSModelGroups
      .Replace("OST_MEP", "") //for OST_MEPSpaces
      .Replace("OST_", "") //for any other OST_blablabla
      .Replace("_", " ");

    var res = Enum.TryParse(cleanName, out SOBR.RevitCategory cat);
    if (!res)
    {
      throw new ConversionException($"Built-in category {builtInCategory} is not supported.");
    }

    return cat;
  }

  public static BuiltInCategory GetBuiltInCategory(this Category category)
  {
#if REVIT2024_OR_GREATER
    return (BuiltInCategory)category.Id.Value;
#else
    return (BuiltInCategory)category.Id.IntegerValue;
#endif
  }

  public static string GetBuiltInFromSchemaBuilderCategory(this SOBR.RevitCategory c)
  {
    var name = Enum.GetName(typeof(SOBR.RevitCategory), c);
    return $"OST_{name}";
  }

  public static string GetBuiltInFromSchemaBuilderCategory(this SOBR.RevitFamilyCategory c)
  {
    var name = Enum.GetName(typeof(SOBR.RevitFamilyCategory), c);
    return $"OST_{name}";
  }
}
