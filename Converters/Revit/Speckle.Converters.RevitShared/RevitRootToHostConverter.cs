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
  private readonly ITypedConverter<Base, object> _documentationOrBaseToGeometryConverter;

  public RevitRootToHostConverter(
    ITypedConverter<Base, object> documentationOrBaseToGeometryConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _documentationOrBaseToGeometryConverter = documentationOrBaseToGeometryConverter;
    _converterSettings = converterSettings;
  }

  public object Convert(Base target)
  {
    var result = _documentationOrBaseToGeometryConverter.Convert(target);
    var definitionId = target.applicationId ?? target.id.NotNull();

    if (result is List<GeometryObject> geometryObjects) // 3d objects converted
    {
      if (geometryObjects.Count == 0)
      {
        throw new ConversionException($"No supported conversion for {target.speckle_type} found.");
      }

      DirectShapeLibrary
        .GetDirectShapeLibrary(_converterSettings.Current.Document)
        .AddDefinition(definitionId, geometryObjects);

      return new DirectShapeDefinitionWrapper(definitionId, geometryObjects);
    }

    if (result is List<string> geometryIds) // documentation elements converted
    {
      return geometryIds;
    }
    throw new ConversionException($"No supported conversion for {target.speckle_type} found.");
  }
}
