namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Base interface for property extraction from CSi objects.
/// Forms part of a hierarchy for separating common CSi properties from product-specific properties.
/// </summary>
public interface IPropertyExtractor
{
  /// <summary>
  /// Extracts properties from a CSi wrapper object.
  /// </summary>
  /// <param name="wrapper">The CSi wrapper object to extract properties from</param>
  /// <returns>Dictionary of extracted properties, or null if no properties are available</returns>
  Dictionary<string, object?>? ExtractProperties(ICsiWrapper wrapper);
}

/// <summary>
/// Interface for extracting properties common to all CSi products (SAP2000, ETABS).
/// Implemented by CsiGeneralPropertiesExtractor.
/// </summary>
/// <remarks>
/// Properties extracted through this interface should be available through identical API calls
/// across all CSi products.
/// </remarks>
public interface IGeneralPropertyExtractor : IPropertyExtractor { }

/// <summary>
/// Interface for extracting product-specific properties (e.g., ETABS-specific properties).
/// Implemented by product-specific extractors like EtabsClassPropertiesExtractor.
/// </summary>
/// <remarks>
/// Properties extracted through this interface are specific to individual CSi products
/// and use product-specific API calls.
/// </remarks>
public interface IClassPropertyExtractor : IPropertyExtractor { }
