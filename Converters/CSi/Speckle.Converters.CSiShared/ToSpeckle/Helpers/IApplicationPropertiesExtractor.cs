namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class PropertyExtractionResult
{
  public string Name { get; set; }
  public string Type { get; set; }
  public string ApplicationId { get; set; }
  public Dictionary<string, object?> Properties { get; set; }
}

/// <summary>
/// Interface for extracting application-specific properties (e.g., ETABS-specific properties).
/// Implementations must compose with SharedPropertiesExtractor to ensure both shared and
/// application-specific properties are extracted.
/// </summary>
public interface IApplicationPropertiesExtractor
{
  SharedPropertiesExtractor SharedPropertiesExtractor { get; }
  PropertyExtractionResult ExtractProperties(ICsiWrapper wrapper);
}
