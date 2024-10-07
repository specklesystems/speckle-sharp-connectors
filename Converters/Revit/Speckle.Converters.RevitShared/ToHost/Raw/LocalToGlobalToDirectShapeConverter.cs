using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Spirit of the LocalToGlobalMap, we can't pass that object directly here bc it lives in Connectors.Common which I (ogu) don't want to bother with it.
/// </summary>
public class LocalToGlobalToDirectShapeConverter
  : ITypedConverter<(Base atomicObject, List<Matrix4x4> matrix), DB.DirectShape>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<(Matrix4x4 matrix, string units), DB.Transform> _transformConverter;

  public LocalToGlobalToDirectShapeConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<(Matrix4x4 matrix, string units), DB.Transform> transformConverter
  )
  {
    _converterSettings = converterSettings;
    _transformConverter = transformConverter;
  }

  public DB.DirectShape Convert((Base atomicObject, List<Matrix4x4> matrix) target)
  {
    // 1- set ds category
    var category = target.atomicObject["category"] as string;
    var dsCategory = DB.BuiltInCategory.OST_GenericModel;
    if (category is string categoryString)
    {
      var res = Enum.TryParse($"OST_{categoryString}", out DB.BuiltInCategory cat);
      if (res)
      {
        var c = DB.Category.GetCategory(_converterSettings.Current.Document, cat);
        if (c is not null && DB.DirectShape.IsValidCategoryId(c.Id, _converterSettings.Current.Document))
        {
          dsCategory = cat;
        }
      }
    }

    // 2 - init DirectShape
    var result = DB.DirectShape.CreateElement(_converterSettings.Current.Document, new DB.ElementId(dsCategory));

    // 3 - Transform the geometries
    DB.Transform combinedTransform = DB.Transform.Identity;

    // existence of units is must, to be able to scale the transform correctly
    if (target.atomicObject["units"] is string units)
    {
      foreach (Matrix4x4 matrix in target.matrix)
      {
        DB.Transform revitTransform = _transformConverter.Convert((matrix, units));
        combinedTransform = combinedTransform.Multiply(revitTransform);
      }
    }

    var transformedGeometries = DB.DirectShape.CreateGeometryInstance(
      _converterSettings.Current.Document,
      target.atomicObject.applicationId ?? target.atomicObject.id,
      combinedTransform
    );

    // 4- check for valid geometry
    if (!result.IsValidShape(transformedGeometries))
    {
      _converterSettings.Current.Document.Delete(result.Id);
      throw new SpeckleConversionException("Invalid geometry (eg unbounded curves) found for creating directshape.");
    }

    // 5 - This is where we apply the geometries into direct shape.
    result.SetShape(transformedGeometries);

    return result;
  }
}
