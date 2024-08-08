using Speckle.Objects.BuiltElements.Revit;

namespace Speckle.Converters.RevitShared.Helpers;

public interface IRevitCategories
{
  string GetBuiltInFromSchemaBuilderCategory(RevitCategory c);
  RevitCategory GetSchemaBuilderCategoryFromBuiltIn(string builtInCategory);
}
