using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;
using static Speckle.Converter.Navisworks.Helpers.GeometryHelpers;
using static Speckle.Converter.Navisworks.Settings.OriginMode;
using static Speckle.Converter.Navisworks.Settings.RepresentationMode;

namespace Speckle.Converter.Navisworks.Settings;

[GenerateAutoInterface]
public class NavisworksConversionSettingsFactory : INavisworksConversionSettingsFactory
{
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore;
  private readonly ILogger<NavisworksConversionSettingsFactory> _logger;
  private readonly IHostToSpeckleUnitConverter<NAV.Units> _unitsConverter;

  private NAV.Document _document;
  private NAV.BoundingBox3D _modelBoundingBox;
  private bool _convertHiddenElements;

  public NavisworksConversionSettingsFactory(
    IHostToSpeckleUnitConverter<NAV.Units> unitsConverter,
    IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
    ILogger<NavisworksConversionSettingsFactory> logger
  )
  {
    _logger = logger;
    _settingsStore = settingsStore;
    _unitsConverter = unitsConverter;
  }

  public NavisworksConversionSettings Current => _settingsStore.Current;

  private bool _includeInternalProperties;
  private bool _coalescePropertiesFromFirstObjectAncestor;
  private RepresentationMode _visualRepresentationMode;
  private OriginMode _originMode;
  private bool _excludeProperties;

  private static readonly NAV.Vector3D s_canonicalUp = new(0, 0, 1);

  /// <summary>
  /// Creates a new instance of NavisworksConversionSettings with calculated values.
  /// </summary>
  /// <exception cref="InvalidOperationException">
  /// Thrown when no active document is found or document units cannot be converted.
  /// </exception>
  public NavisworksConversionSettings Create()
  {
    // Default settings until overriding them in UI is implemented
    _convertHiddenElements = false;
    _includeInternalProperties = false;
    _coalescePropertiesFromFirstObjectAncestor = true;
    _visualRepresentationMode = ACTIVE;
    _originMode = MODEL_ORIGIN;
    _excludeProperties = false;

    // Derived settings from the active document
    _document = NavisworksApp.ActiveDocument ?? throw new InvalidOperationException("No active document found.");
    _logger.LogInformation("Creating settings for document: {DocumentName}", _document.Title);

    _modelBoundingBox =
      _document.GetBoundingBox(_convertHiddenElements)
      ?? throw new InvalidOperationException("Bounding box could not be determined.");

    var units = _unitsConverter.ConvertOrThrow(_document.Units);
    if (string.IsNullOrEmpty(units))
    {
      throw new InvalidOperationException("Document units could not be converted.");
    }

    // Calculate the transformation vector based on the origin mode
    var transformVector = CalculateTransformVector();
    var isUpright = VectorMatch(_document.UpVector, s_canonicalUp);

    return new NavisworksConversionSettings(
      Document: _document,
      SpeckleUnits: units,
      OriginMode: _originMode,
      IncludeInternalProperties: _includeInternalProperties,
      ConvertHiddenElements: _convertHiddenElements,
      VisualRepresentationMode: _visualRepresentationMode,
      CoalescePropertiesFromFirstObjectAncestor: _coalescePropertiesFromFirstObjectAncestor,
      TransformVector: transformVector,
      IsUpright: isUpright,
      ModelBoundingBox: _modelBoundingBox,
      ExcludeProperties: _excludeProperties
    );
  }

  private NAV.Vector3D CalculateTransformVector() =>
    _originMode switch
    {
      PROJECT_BASE_ORIGIN => CalculateProjectBasePointTransform(),
      BOUNDING_BOX_ORIGIN => CalculateBoundingBoxTransform(),
      MODEL_ORIGIN => new NAV.Vector3D(0, 0, 0), // Default identity transform
      _ => throw new NotSupportedException($"OriginMode {_originMode} is not supported.")
    };

  /// <summary>
  /// Calculates the transformation vector based on the project base point.
  /// </summary>
  /// <returns>The calculated transformation vector.</returns>
  /// <remarks>
  /// This uses mocked project base point data and should be replaced with actual logic
  /// when finally integrating with UI or external configurations.
  /// </remarks>
  private NAV.Vector3D CalculateProjectBasePointTransform()
  {
    // TODO: Replace with actual logic to fetch project base point and units from UI or settings
    using var projectBasePoint = new NAV.Vector3D(10, 20, 0);
    var projectBasePointUnits = NAV.Units.Meters;

    var scale = NAV.UnitConversion.ScaleFactor(projectBasePointUnits, _document.Units);

    // The transformation vector is the negative of the project base point, scaled to the source units.
    // These units are independent of the Speckle units, and because they are from user input.
    return new NAV.Vector3D(-projectBasePoint.X * scale, -projectBasePoint.Y * scale, 0);
  }

  /// <summary>
  /// Calculates the transformation vector based on the bounding box center offset from the origin.
  /// </summary>
  /// <returns>The calculated transformation vector.</returns>
  /// <remarks>
  /// This uses the document active model bounding box center as the base point for the transformation.
  /// </remarks>
  private NAV.Vector3D CalculateBoundingBoxTransform() =>
    new(-_modelBoundingBox.Center.X, -_modelBoundingBox.Center.Y, 0);
}
