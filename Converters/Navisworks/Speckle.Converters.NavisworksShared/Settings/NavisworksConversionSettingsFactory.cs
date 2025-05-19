using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Geometry;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converter.Navisworks.Settings;

[GenerateAutoInterface]
public class NavisworksConversionSettingsFactory : INavisworksConversionSettingsFactory
{
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore;
  private readonly ILogger<NavisworksConversionSettingsFactory> _logger;
  private readonly IHostToSpeckleUnitConverter<NAV.Units> _unitsConverter;

  private NAV.Document? _document;
  private SafeBoundingBox _modelBoundingBox;
  private bool _convertHiddenElements;
  private VisualModes _visualModes;

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

  private static readonly NAV.Vector3D s_canonicalUp = new(0, 0, 1);

  private OriginMode _originMode;

  /// <summary>
  /// Creates a new instance of NavisworksConversionSettings with calculated values.
  /// </summary>
  /// <exception cref="InvalidOperationException">
  /// Thrown when no active document is found or document units cannot be converted.
  /// </exception>
  public NavisworksConversionSettings Create(
    OriginMode originMode,
    RepresentationMode visualRepresentationMode,
    bool convertHiddenElements,
    bool includeInternalProperties,
    bool preserveModelHierarchy
  )
  {
    _convertHiddenElements = convertHiddenElements;
    _originMode = originMode;

    // Initialize document and validate
    InitializeDocument();

    if (_document == null)
    {
      throw new InvalidOperationException("No active document found.");
    }

    var units = _unitsConverter.ConvertOrThrow(_document.Units);

    if (string.IsNullOrEmpty(units))
    {
      throw new InvalidOperationException("Document units could not be converted.");
    }

    // Calculate the transformation vector based on the origin mode
    var transformVector = CalculateTransformVector();
    var isUpright = GeometryHelpers.VectorMatch(_document.UpVector, s_canonicalUp);

    return new NavisworksConversionSettings(
      // Derived from Navisworks Application
      new Derived(
        Document: _document,
        ModelBoundingBox: _modelBoundingBox,
        TransformVector: transformVector,
        IsUpright: isUpright,
        SpeckleUnits: units,
        VisualModes: _visualModes
      ),
      // Optional settings for conversion to be offered in UI
      new User(
        OriginMode: _originMode,
        IncludeInternalProperties: includeInternalProperties,
        ConvertHiddenElements: _convertHiddenElements,
        VisualRepresentationMode: visualRepresentationMode,
        CoalescePropertiesFromFirstObjectAncestor: false, // Not yet exposed in the UI
        ExcludeProperties: false, // Not yet exposed in the UI
        PreserveModelHierarchy: preserveModelHierarchy
      )
    );
  }

  private void InitializeDocument()
  {
    _document = NavisworksApp.ActiveDocument ?? throw new InvalidOperationException("No active document found.");
    _logger.LogInformation("Creating settings for document: {DocumentName}", _document.Title);
    _modelBoundingBox = new SafeBoundingBox(_document.GetBoundingBox(_convertHiddenElements));
    _visualModes = VisualModeCheck.Current;
  }

  private SafeVector CalculateTransformVector() =>
    _originMode switch
    {
      OriginMode.ProjectBasePoint => CalculateProjectBasePointTransform(),
      OriginMode.BoundingBoxCenter => CalculateBoundingBoxTransform(),
      OriginMode.ModelOrigin => new SafeVector(0.0, 0.0, 0.0), // Default identity transform
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
  private SafeVector CalculateProjectBasePointTransform()
  {
    // TODO: Replace with actual logic to fetch project base point and units from UI or settings
    var projectBasePoint = new SafeVector(10, 20, 0);
    // ReSharper disable once ConvertToConstant.Local
    var projectBasePointUnits = NAV.Units.Meters;

    var scale = NAV.UnitConversion.ScaleFactor(projectBasePointUnits, _document!.Units);

    // The transformation vector is the negative of the project base point, scaled to the source units.
    // These units are independent of the Speckle units, and because they are from user input.
    return new SafeVector(-projectBasePoint.X * scale, -projectBasePoint.Y * scale, 0);
  }

  /// <summary>
  /// Calculates the transformation vector based on the bounding box center offset from the origin.
  /// </summary>
  /// <returns>The calculated transformation vector.</returns>
  /// <remarks>
  /// This uses the document active model bounding box center as the base point for the transformation.
  /// Assumes no translation in the Z-axis.
  /// </remarks>
  private SafeVector CalculateBoundingBoxTransform() =>
    new(-_modelBoundingBox.Center.X, -_modelBoundingBox.Center.Y, 0);
}
