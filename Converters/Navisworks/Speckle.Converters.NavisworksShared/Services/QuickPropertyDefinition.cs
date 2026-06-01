namespace Speckle.Converter.Navisworks.Services;

public sealed class QuickPropertyDefinition
{
  public QuickPropertyDefinition(
    string categoryDisplayName,
    string? propertyDisplayName,
    string? categoryInternalName,
    string? propertyInternalName
  )
  {
    CategoryDisplayName = categoryDisplayName;
    PropertyDisplayName = propertyDisplayName;
    CategoryInternalName = categoryInternalName;
    PropertyInternalName = propertyInternalName;
  }

  public string CategoryDisplayName { get; }

  public string? PropertyDisplayName { get; }

  public bool IsCategoryOnly => string.IsNullOrWhiteSpace(PropertyDisplayName);

  public string? CategoryInternalName { get; }

  public string? PropertyInternalName { get; }
}
