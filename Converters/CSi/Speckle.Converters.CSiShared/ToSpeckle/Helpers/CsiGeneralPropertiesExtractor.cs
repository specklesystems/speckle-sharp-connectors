namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// The "coordinator" for extracting properties common across CSi products (Etabs and Sap2000).
/// </summary>
/// <remarks>
/// Design Philosophy:
/// - Utilizes sub-extractors for each wrapper type (frame, joint, shell) injected via Dependency Injection.
/// - Relies on public sub-extractors for handling CSi API calls specific to each type.
/// Workflow:
/// 1. Receives an ICsiWrapper through ExtractProperties.
/// 2. Routes the request to the appropriate sub-extractor based on the wrapper's concrete type.
/// 3. Returns properties common to all CSi products.
/// Integration:
/// - Part of the property extraction hierarchy.
/// - Works alongside product-specific extractors (e.g., EtabsClassPropertiesExtractor).
/// - Properties extracted here are available in both SAP2000 and ETABS.
/// Changes in Access Modifier:
/// - Sub-extractors (CsiFramePropertiesExtractor, CsiJointPropertiesExtractor, CsiShellPropertiesExtractor) are now public to allow DI container registration.
/// </remarks>
public class CsiGeneralPropertiesExtractor : IGeneralPropertyExtractor
{
  private readonly CsiFramePropertiesExtractor _csiFramePropertiesExtractor;
  private readonly CsiJointPropertiesExtractor _csiJointPropertiesExtractor;
  private readonly CsiShellPropertiesExtractor _csiShellPropertiesExtractor;

  /// <summary>
  /// Initializes a new instance of the <see cref="CsiGeneralPropertiesExtractor"/> class.
  /// </summary>
  /// <param name="csiFramePropertiesExtractor">The extractor for frame-specific properties.</param>
  /// <param name="csiJointPropertiesExtractor">The extractor for joint-specific properties.</param>
  /// <param name="csiShellPropertiesExtractor">The extractor for shell-specific properties.</param>
  /// <remarks>
  /// The sub-extractors are resolved by the DI container and injected into this class.
  /// </remarks>
  public CsiGeneralPropertiesExtractor(
    CsiFramePropertiesExtractor csiFramePropertiesExtractor,
    CsiJointPropertiesExtractor csiJointPropertiesExtractor,
    CsiShellPropertiesExtractor csiShellPropertiesExtractor
  )
  {
    _csiFramePropertiesExtractor = csiFramePropertiesExtractor;
    _csiJointPropertiesExtractor = csiJointPropertiesExtractor;
    _csiShellPropertiesExtractor = csiShellPropertiesExtractor;
  }

  /// <summary>
  /// Routes property extraction requests to the appropriate sub-extractor based on the wrapper type.
  /// </summary>
  /// <param name="wrapper">Wrapper object representing a CSi element (Frame, Joint, Shell).</param>
  /// <returns>
  /// A dictionary containing properties common to all CSi products, or null if the wrapper type is unsupported.
  /// </returns>
  /// <remarks>
  /// Currently supported wrapper types:
  /// - <see cref="CsiFrameWrapper"/>: Properties from FrameObj API calls.
  /// - <see cref="CsiJointWrapper"/>: Properties from PointObj API calls.
  /// - <see cref="CsiShellWrapper"/>: Properties from AreaObj API calls.
  /// </remarks>
  public void ExtractProperties(ICsiWrapper wrapper, Dictionary<string, object?> properties)
  {
    switch (wrapper)
    {
      case CsiFrameWrapper frame:
        _csiFramePropertiesExtractor.ExtractProperties(frame, properties);
        break;
      case CsiJointWrapper joint:
        _csiJointPropertiesExtractor.ExtractProperties(joint, properties);
        break;
      case CsiShellWrapper shell:
        _csiShellPropertiesExtractor.ExtractProperties(shell, properties);
        break;
    }
  }
}
