namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

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
  public Dictionary<string, object?> GetProperties(ICsiWrapper wrapper)
  {
    var properties = new Dictionary<string, object?>();

    var generalProps = _generalPropertyExtractor.ExtractProperties(wrapper);
    if (generalProps != null)
    {
      properties["General Properties"] = generalProps; // TODO: Think about naming here
    }

    var classProps = _classPropertyExtractor.ExtractProperties(wrapper);
    if (classProps != null)
    {
      properties["Class Properties"] = classProps; // TODO: Think about naming here
    }

    return properties;
  }
}
