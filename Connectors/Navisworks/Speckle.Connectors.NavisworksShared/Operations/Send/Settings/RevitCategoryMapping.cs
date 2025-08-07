using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Operations.Send.Settings;

public class RevitCategoryMappingSetting(bool value) : ICardSetting
{
  public string? Id { get; set; } = "mappingToRevitCategories";
  public string? Title { get; set; } = "Map to Revit Categories";
  public string? Type { get; set; } = "boolean";
  public List<string>? Enum { get; set; }
  public object? Value { get; set; } = value;
}
