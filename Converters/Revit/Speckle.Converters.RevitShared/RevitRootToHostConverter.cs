using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public class RevitRootToHostConverter : IRootToHostConverter
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;
  private readonly ITypedConverter<Base, List<DB.GeometryObject>> _baseToGeometryConverter;

  public RevitRootToHostConverter(
    ITypedConverter<Base, List<DB.GeometryObject>> baseToGeometryConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    RevitToHostCacheSingleton revitToHostCacheSingleton
  )
  {
    _baseToGeometryConverter = baseToGeometryConverter;
    _converterSettings = converterSettings;
    _revitToHostCacheSingleton = revitToHostCacheSingleton;
  }

  public object Convert(Base target)
  {
    List<DB.GeometryObject> geometryObjects = _baseToGeometryConverter.Convert(target);

    if (geometryObjects.Count == 0)
    {
      throw new SpeckleConversionException($"No supported conversion for {target.speckle_type} found.");
    }

    // create direct shape from geometries
    DB.DirectShape result = CreateDirectShape(geometryObjects, target, target["category"] as string);

    return result;
  }

  private DB.DirectShape CreateDirectShape(List<GeometryObject> geometry, Base originalObject, string? category)
  {
    // set ds category
    var dsCategory = BuiltInCategory.OST_GenericModel;
    if (category is string categoryString)
    {
      var res = Enum.TryParse($"OST_{categoryString}", out DB.BuiltInCategory cat);
      if (res)
      {
        var c = Category.GetCategory(_converterSettings.Current.Document, cat);
        if (c is not null && DirectShape.IsValidCategoryId(c.Id, _converterSettings.Current.Document))
        {
          dsCategory = cat;
        }
      }
    }

    var result = DirectShape.CreateElement(_converterSettings.Current.Document, new DB.ElementId(dsCategory));

    // check for valid geometry
    if (!result.IsValidShape(geometry))
    {
      _converterSettings.Current.Document.Delete(result.Id);
      throw new SpeckleConversionException("Invalid geometry (eg unbounded curves) found for creating directshape.");
    }

    result.SetShape(geometry);

    if (originalObject is SOG.IRawEncodedObject)
    {
      var materialId = DB.ElementId.InvalidElementId;
      if (
        _revitToHostCacheSingleton.MaterialsByObjectId.TryGetValue(originalObject.applicationId ?? originalObject.id, out var mappedElementId)
      )
      {
        materialId = mappedElementId;
      }
      
      // if(materialId == DB.ElementId.InvalidElementId) 
      
      foreach (var geo in geometry)
      {
        if (geo is Face f)
        {
          _converterSettings.Current.Document.Paint(result.Id, f, materialId);
        }
      }
    }
    
    return result;
  }
}
