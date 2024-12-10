namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public record PropertyExtractionResult(
  string Name,
  string Type,
  string ApplicationId,
  Dictionary<string, object?> Properties
);

/// <summary>
/// Main orchestrator for combining general Csi properties with product-specific properties.
/// Uses composition to combine results from multiple property extractors.
/// </summary>
/// <remarks>
/// Implementation Notes:
/// - The "applicationId" property is extracted during the general property extraction phase
/// - It is removed from the properties dictionary as it needs to (desired) be handled at the object root level
/// - This approach (not ideal) maintains consistent method signatures across CSiShared and product-specific libraries
/// - First instinct is understandably "feels like a leaky abstraction and could be refactored to be more explicit"
/// </remarks>
public class PropertiesExtractor
{
  private readonly IGeneralPropertyExtractor _generalPropertyExtractor;
  private readonly IClassPropertyExtractor _classPropertyExtractor;

  public PropertiesExtractor(
    IGeneralPropertyExtractor generalPropertyExtractor,
    IClassPropertyExtractor classPropertyExtractor
  )
  {
    _generalPropertyExtractor = generalPropertyExtractor;
    _classPropertyExtractor = classPropertyExtractor;
  }

  /// <summary>
  /// Combines properties from both general and product-specific extractors.
  /// </summary>
  /// <param name="wrapper">The CSi wrapper to extract properties from</param>
  /// <returns>Combined dictionary of all extracted properties</returns>
  public PropertyExtractionResult GetProperties(ICsiWrapper wrapper)
  {
    // Single dictionary populated by respective extractors
    var properties = new Dictionary<string, object?>();

    // Extractors do their thing
    _generalPropertyExtractor.ExtractProperties(wrapper, properties);
    _classPropertyExtractor.ExtractProperties(wrapper, properties);

    // Capture "base" properties
    properties.TryGetValue("applicationId", out var guid);
    var applicationId = guid?.ToString() ?? string.Empty;

    // The "base" properties are removed from the dictionary (they sit at object root)
    properties.Remove("applicationId");

    return new(wrapper.Name, wrapper.ObjectName, applicationId, properties);
  }
}
