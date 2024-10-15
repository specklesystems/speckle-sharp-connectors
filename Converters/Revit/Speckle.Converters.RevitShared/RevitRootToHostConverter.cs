using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public record DirectShapeDefinitionWrapper(string DefinitionId, List<GeometryObject> Geometries);

public class RevitRootToHostConverter : IRootToHostConverter
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<Base, List<DB.GeometryObject>> _baseToGeometryConverter;

  public RevitRootToHostConverter(
    ITypedConverter<Base, List<DB.GeometryObject>> baseToGeometryConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _baseToGeometryConverter = baseToGeometryConverter;
    _converterSettings = converterSettings;
  }

  public object Convert(Base target)
  {
    List<GeometryObject> geometryObjects = _baseToGeometryConverter.Convert(target);

    if (geometryObjects.Count == 0)
    {
      throw new SpeckleConversionException($"No supported conversion for {target.speckle_type} found.");
    }

    var definitionId = target.applicationId ?? target.id;
    DirectShapeLibrary
      .GetDirectShapeLibrary(_converterSettings.Current.Document)
      .AddDefinition(definitionId, geometryObjects);

    return new DirectShapeDefinitionWrapper(definitionId, geometryObjects);
  }
}
