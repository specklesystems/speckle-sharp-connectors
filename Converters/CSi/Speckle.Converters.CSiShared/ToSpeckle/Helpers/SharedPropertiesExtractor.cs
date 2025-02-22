namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts properties common to all CSi products (SAP2000, ETABS).
/// </summary>
public class SharedPropertiesExtractor
{
  private readonly CsiFramePropertiesExtractor _csiFramePropertiesExtractor;
  private readonly CsiJointPropertiesExtractor _csiJointPropertiesExtractor;
  private readonly CsiShellPropertiesExtractor _csiShellPropertiesExtractor;

  /// <summary>
  /// Initializes a new instance of the <see cref="SharedPropertiesExtractor"/> class.
  /// </summary>
  /// <param name="csiFramePropertiesExtractor">The extractor for frame-specific properties.</param>
  /// <param name="csiJointPropertiesExtractor">The extractor for joint-specific properties.</param>
  /// <param name="csiShellPropertiesExtractor">The extractor for shell-specific properties.</param>
  /// <remarks>
  /// The sub-extractors are resolved by the DI container and injected into this class.
  /// </remarks>
  public SharedPropertiesExtractor(
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
  /// Extracts properties from a CSi element wrapper, delegating to the appropriate sub-extractor based on the wrapper type.
  /// </summary>
  /// <param name="wrapper">
  /// A <see cref="ICsiWrapper"/> representing a CSi element (Frame, Joint, or Shell).
  /// </param>
  /// <returns>
  /// A <see cref="PropertyExtractionResult"/> containing common and specific properties of the CSi element,
  /// or <c>null</c> if the wrapper type is unsupported.
  /// </returns>
  /// <remarks>
  /// Supported wrapper types:
  /// <see cref="CsiFrameWrapper"/> (FrameObj API),
  /// <see cref="CsiJointWrapper"/> (PointObj API),
  /// and <see cref="CsiShellWrapper"/> (AreaObj API).
  /// </remarks>
  public PropertyExtractionResult Extract(ICsiWrapper wrapper)
  {
    var objectData = new PropertyExtractionResult
    {
      Name = wrapper.Name,
      Type = wrapper.ObjectName,
      ApplicationId = string.Empty, // Populated in ExtractProperties
      Properties = new Dictionary<string, object?>()
    };

    switch (wrapper)
    {
      case CsiJointWrapper joint:
        _csiJointPropertiesExtractor.ExtractProperties(joint, objectData);
        break;
      case CsiFrameWrapper frame:
        _csiFramePropertiesExtractor.ExtractProperties(frame, objectData);
        break;
      case CsiShellWrapper shell:
        _csiShellPropertiesExtractor.ExtractProperties(shell, objectData);
        break;
      case CsiTendonWrapper:
      case CsiLinkWrapper:
      case CsiCableWrapper:
      case CsiSolidWrapper:
        throw new NotImplementedException($"Data extraction for {wrapper.ObjectName} not yet supported.");
      default:
        throw new ArgumentException($"Unsupported wrapper type: {nameof(wrapper)}");
    }

    return objectData;
  }
}
