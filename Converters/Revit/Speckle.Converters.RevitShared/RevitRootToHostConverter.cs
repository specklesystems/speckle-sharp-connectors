using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public record DirectShapeDefinitionWrapper(string DefinitionId, List<GeometryObject> Geometries);

public class RevitRootToHostConverter : IRootToHostConverter
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<Base, List<DB.GeometryObject>> _baseToGeometryConverter;
  private readonly ITypedConverter<Base, List<string>> _planViewToGeometryConverter;

  public RevitRootToHostConverter(
    ITypedConverter<Base, List<string>> planViewToGeometryConverter,
    ITypedConverter<Base, List<DB.GeometryObject>> baseToGeometryConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _planViewToGeometryConverter = planViewToGeometryConverter;
    _baseToGeometryConverter = baseToGeometryConverter;
    _converterSettings = converterSettings;
  }

  public object Convert(Base target)
  {
    // If ActiveView is a Plan view, use PlanView converter (will ignore DirectShapes)
    View activeView = _converterSettings.Current.Document.ActiveView;
    if (activeView.ViewType == ViewType.FloorPlan)
    {
      return _planViewToGeometryConverter.Convert(target);
    }

    // Otherwise, use default behavior and covert everything to DirectShapes
    List<DB.GeometryObject> geometryObjects = _baseToGeometryConverter.Convert(target);

    if (geometryObjects.Count == 0)
    {
      throw new ConversionException($"No supported conversion for {target.speckle_type} found.");
    }

    var definitionId = target.applicationId ?? target.id.NotNull();
    DirectShapeLibrary
      .GetDirectShapeLibrary(_converterSettings.Current.Document)
      .AddDefinition(definitionId, geometryObjects);

    return new DirectShapeDefinitionWrapper(definitionId, geometryObjects);
  }
}
