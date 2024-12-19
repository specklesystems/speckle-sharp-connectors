using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public record DirectShapeDefinitionWrapper(string DefinitionId, List<GeometryObject> Geometries);

public class RevitRootToHostConverter(
  ITypedConverter<Base, List<DB.GeometryObject>> baseToGeometryConverter,
  IConverterSettingsStore<RevitConversionSettings> converterSettings)
  : IRootToHostConverter
{
  public HostResult Convert(Base target)
  {
    List<GeometryObject> geometryObjects = baseToGeometryConverter.Convert(target);

    if (geometryObjects.Count == 0)
    {
      HostResult.NoConversion($"No supported conversion for {target.speckle_type} found.");
    }

    var definitionId = target.applicationId ?? target.id.NotNull();
    DirectShapeLibrary
      .GetDirectShapeLibrary(converterSettings.Current.Document)
      .AddDefinition(definitionId, geometryObjects);

    return HostResult.Success(new DirectShapeDefinitionWrapper(definitionId, geometryObjects));
  }
}
