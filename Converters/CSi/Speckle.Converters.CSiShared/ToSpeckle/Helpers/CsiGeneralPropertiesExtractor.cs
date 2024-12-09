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
/// - Sub-extractors (FramePropertiesExtractor, JointPropertiesExtractor, ShellPropertiesExtractor) are now public to allow DI container registration.
/// </remarks>
public class CsiGeneralPropertiesExtractor : IGeneralPropertyExtractor
{
  private readonly FramePropertiesExtractor _framePropertiesExtractor;
  private readonly JointPropertiesExtractor _jointPropertiesExtractor;
  private readonly ShellPropertiesExtractor _shellPropertiesExtractor;

  /// <summary>
  /// Initializes a new instance of the <see cref="CsiGeneralPropertiesExtractor"/> class.
  /// </summary>
  /// <param name="framePropertiesExtractor">The extractor for frame-specific properties.</param>
  /// <param name="jointPropertiesExtractor">The extractor for joint-specific properties.</param>
  /// <param name="shellPropertiesExtractor">The extractor for shell-specific properties.</param>
  /// <remarks>
  /// The sub-extractors are resolved by the DI container and injected into this class.
  /// </remarks>
  public CsiGeneralPropertiesExtractor(
    FramePropertiesExtractor framePropertiesExtractor,
    JointPropertiesExtractor jointPropertiesExtractor,
    ShellPropertiesExtractor shellPropertiesExtractor
  )
  {
    _framePropertiesExtractor = framePropertiesExtractor;
    _jointPropertiesExtractor = jointPropertiesExtractor;
    _shellPropertiesExtractor = shellPropertiesExtractor;
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
  public Dictionary<string, object?>? ExtractProperties(ICsiWrapper wrapper)
  {
    return wrapper switch
    {
      CsiFrameWrapper frame => _framePropertiesExtractor.ExtractProperties(frame),
      CsiJointWrapper joint => _jointPropertiesExtractor.ExtractProperties(joint),
      CsiShellWrapper shell => _shellPropertiesExtractor.ExtractProperties(shell),
      _ => null
    };
  }
}
