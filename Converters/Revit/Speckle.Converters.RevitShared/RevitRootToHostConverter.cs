using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Data;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public record DirectShapeDefinitionWrapper(string DefinitionId, List<GeometryObject> Geometries);

public class RevitRootToHostConverter : IRootToHostConverter
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<SOG.Region, string> _regionToFilledRegionConverter;
  private readonly ITypedConverter<Base, List<DB.GeometryObject>> _baseToGeometryConverter;

  public RevitRootToHostConverter(
    ITypedConverter<Base, List<DB.GeometryObject>> baseToGeometryConverter,
    ITypedConverter<SOG.Region, string> regionToFilledRegionConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _baseToGeometryConverter = baseToGeometryConverter;
    _regionToFilledRegionConverter = regionToFilledRegionConverter;
    _converterSettings = converterSettings;
  }

  public object Convert(Base target)
  {
    List<GeometryObject> geometryObjects = new();
    List<string> regionIds = new();

    switch (target)
    {
      case SOG.Region region:
        // return ElementId of created region
        var result = ConvertRegionNativeOrFallback(region, region);
        if (result is string elementId)
        {
          regionIds.Add(elementId);
          return regionIds;
        }
        // use fallback to direct shape conversion if unsuccessful (e.g. ActiveView was not 2d)
        geometryObjects.AddRange((List<DB.GeometryObject>)result);
        break;

      case DataObject dataObj:
        if (dataObj.displayValue.All(x => x is SOG.Region))
        {
          foreach (var displayRegion in dataObj.displayValue)
          {
            var resultDisplayRegion = ConvertRegionNativeOrFallback((SOG.Region)displayRegion, dataObj);
            if (resultDisplayRegion is string elementIdDisplayRegion)
            {
              regionIds.Add(elementIdDisplayRegion);
            }
            else
            {
              geometryObjects.AddRange((List<DB.GeometryObject>)resultDisplayRegion);
              // break here, because fallback converter converted the entire object
              break;
            }
          }

          if (geometryObjects.Count == 0)
          {
            return regionIds;
          }
        }
        geometryObjects = _baseToGeometryConverter.Convert(target);
        break;

      default:
        geometryObjects = _baseToGeometryConverter.Convert(target);
        break;
    }

    var definitionId = target.applicationId ?? target.id.NotNull();
    DirectShapeLibrary
      .GetDirectShapeLibrary(_converterSettings.Current.Document)
      .AddDefinition(definitionId, geometryObjects);

    return new DirectShapeDefinitionWrapper(definitionId, geometryObjects);
  }

  private object ConvertRegionNativeOrFallback(SOG.Region region, Base originalObj)
  {
    try
    {
      return _regionToFilledRegionConverter.Convert(region);
    }
    catch (Autodesk.Revit.Exceptions.ArgumentException)
    {
      return _baseToGeometryConverter.Convert(originalObj);
    }
  }
}
