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
    
    DB.DirectShapeLibrary.GetDirectShapeLibrary(_converterSettings.Current.Document).AddDefinition(target.applicationId ?? target.id, geometryObjects);

    // create direct shape from geometries
    //DB.DirectShape result = CreateDirectShape(geometryObjects, target["category"] as string);
    
    return geometryObjects;
  }

  private DB.DirectShape CreateDirectShape(List<GeometryObject> geometry, string? category)
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

    // if (originalObject is SOG.IRawEncodedObject)
    // {
    //   var materialId = DB.ElementId.InvalidElementId;
    //   if (
    //     _revitToHostCacheSingleton.MaterialsByObjectId.TryGetValue(originalObject.applicationId ?? originalObject.id, out var mappedElementId)
    //   )
    //   {
    //     materialId = mappedElementId;
    //   }
    //   
    //   // if(materialId == DB.ElementId.InvalidElementId) 
    //   var elGeometry = result.get_Geometry(new Options() { DetailLevel = ViewDetailLevel.Undefined });
    //   foreach (var geo in elGeometry)
    //   {
    //     if (geo is Solid s)
    //     {
    //       foreach (Face face in s.Faces)
    //       {
    //         _converterSettings.Current.Document.Paint(result.Id, face, materialId);
    //       }
    //     }
    //   }
    // }
    
    return result;
  }
}
