using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Geometry;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converter.Navisworks.Settings;

[GenerateAutoInterface]
public class NavisworksConversionSettingsFactory(
  IHostToSpeckleUnitConverter<NAV.Units> unitsConverter,
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
  ILogger<NavisworksConversionSettingsFactory> logger)
  : INavisworksConversionSettingsFactory
{
  private NAV.Document? _document;
  private SafeBoundingBox _modelBoundingBox;
  private bool _convertHiddenElements;
  private OriginMode _originMode;

  public NavisworksConversionSettings Current => settingsStore.Current;

  private static readonly NAV.Vector3D s_canonicalUp = new(0, 0, 1);
  public NavisworksConversionSettings Create(
    OriginMode originMode,
    RepresentationMode visualRepresentationMode,
    bool convertHiddenElements,
    bool includeInternalProperties,
    bool preserveModelHierarchy,
    bool mappingToRevitCategories
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

    var units = unitsConverter.ConvertOrThrow(_document.Units);
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
        SpeckleUnits: units
      ),
      // Optional settings for conversion to be offered in UI
      new User(
        OriginMode: _originMode,
        IncludeInternalProperties: includeInternalProperties,
        ConvertHiddenElements: _convertHiddenElements,
        VisualRepresentationMode: visualRepresentationMode,
        CoalescePropertiesFromFirstObjectAncestor: false, // Not yet exposed in the UI
        ExcludeProperties: false, // Not yet exposed in the UI
        PreserveModelHierarchy: preserveModelHierarchy,
        RevitCategoryMapping: mappingToRevitCategories
      )
    );
  }

  private void InitializeDocument()
  {
    _document = NavisworksApp.ActiveDocument ?? throw new InvalidOperationException("No active document found.");
    logger.LogInformation("Creating settings for document: {DocumentName}", _document.Title);
    _modelBoundingBox = new SafeBoundingBox(_document.GetBoundingBox(_convertHiddenElements));
  }

  private SafeVector CalculateTransformVector() =>
    _originMode switch
    {
      OriginMode.ProjectBasePoint => CalculateProjectBasePointTransform(),
      OriginMode.BoundingBoxCenter => CalculateBoundingBoxTransform(),
      OriginMode.ModelOrigin => new SafeVector(0.0, 0.0, 0.0), // Default identity transform
      _ => throw new NotSupportedException($"OriginMode {_originMode} is not supported.")
    };

  private SafeVector CalculateProjectBasePointTransform()
  {
    // WARNING: Mocked data - replace with actual UI/settings when implementing project base point
    var projectBasePoint = new SafeVector(10, 20, 0);
    var projectBasePointUnits = NAV.Units.Meters;
    var scale = NAV.UnitConversion.ScaleFactor(projectBasePointUnits, _document!.Units);
    return new SafeVector(-projectBasePoint.X * scale, -projectBasePoint.Y * scale, 0);
  }

  private SafeVector CalculateBoundingBoxTransform() =>
    new(-_modelBoundingBox.Center.X, -_modelBoundingBox.Center.Y, 0);
}
