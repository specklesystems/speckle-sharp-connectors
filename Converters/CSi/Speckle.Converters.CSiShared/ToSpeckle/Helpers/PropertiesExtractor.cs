namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public record PropertyExtractionResult(
  string Name,
  string Type,
  string ApplicationId,
  Dictionary<string, object?> Properties
);

/// <summary>
/// Main orchestrator for combining general CSi properties with product-specific properties.
/// Uses composition to combine results from multiple property extractors.
/// </summary>
/// <remarks>
/// Architectural Notes:
/// - Composes multiple property extractors following Composition over Inheritance
/// - Uses dependency injection for loose coupling
/// - Maintains single responsibility of combining property results
/// - Preserves separation between CSi common and product-specific properties
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
