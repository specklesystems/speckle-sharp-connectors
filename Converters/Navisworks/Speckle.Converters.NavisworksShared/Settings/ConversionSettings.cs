using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connector.Navisworks.Settings;

public class VisualRepresentationSetting : ICardSetting
{
  public string? Id { get; set; } = "visualRepresentation";
  public string? Title { get; set; } = "Visual Representation";
  public string? Type { get; set; } = "enum";
  public object? Value { get; set; }
  public List<string>? Enum { get; set; } = ["Active", "Original", "Permanent"];
}

public class ConvertHiddenElementsSetting : ICardSetting
{
  public string? Id { get; set; } = "convertHiddenElements";
  public string? Title { get; set; } = "Convert Hidden Elements";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = false;
  public List<string>? Enum { get; set; }
}

public class OriginModeSetting : ICardSetting
{
  public string? Id { get; set; } = "originMode";
  public string? Title { get; set; } = "Origin Mode";
  public string? Type { get; set; } = "enum";
  public object? Value { get; set; }
  public List<string>? Enum { get; set; } = ["ModelOrigin", "ProjectBaseOrigin", "BoundingBoxOrigin"];
}

public class IncludeInternalPropertiesSetting : ICardSetting
{
  public string? Id { get; set; } = "includeInternalProperties";
  public string? Title { get; set; } = "Include Internal Properties";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = false;
  public List<string>? Enum { get; set; }
}
