using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects.Data;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Converts local to global maps to direct shapes.
/// Spirit of the LocalToGlobalMap, we can't pass that object directly here bc it lives in Connectors.Common which I (ogu) don't want to bother with it.
/// All this is  poc that should be burned, once we enable proper block support to revit.
/// </summary>
public class LocalToGlobalToDirectShapeConverter
  : ITypedConverter<(Base atomicObject, IReadOnlyCollection<Matrix4x4> matrix), DB.DirectShape>
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

  public DB.DirectShape Convert((Base atomicObject, IReadOnlyCollection<Matrix4x4> matrix) target)
  {
    // 1- set ds category
    // NOTE: previously, builtInCategory was on the atomicObject level. this was subsequently moved to properties
    string? category = null;

    if (target.atomicObject is DataObject dataObject)
    {
      if (dataObject.properties.TryGetValue("builtInCategory", out var builtInCategory))
      {
        category = builtInCategory?.ToString();
      }
    }

    var dsCategory = DB.BuiltInCategory.OST_GenericModel;
    if (category is not null)
    {
      var res = Enum.TryParse(category, out DB.BuiltInCategory cat);
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

    // If there is no transforms to be applied, use the simple way of creating direct shapes
    if (target.matrix.Count == 0)
    {
      var def = DB
        .DirectShapeLibrary.GetDirectShapeLibrary(_converterSettings.Current.Document)
        .FindDefinition(target.atomicObject.applicationId ?? target.atomicObject.id.NotNull());
      result.SetShape(def);
      return result; // note fast exit here
    }

    // 3 - Transform the geometries
    DB.Transform combinedTransform = DB.Transform.Identity;

    // existence of units is must, to be able to scale the transform correctly
    if (target.atomicObject["units"] is string units)
    {
      foreach (Matrix4x4 matrix in target.matrix.Reverse())
      {
        DB.Transform revitTransform = _transformConverter.Convert((matrix, units));
        combinedTransform = combinedTransform.Multiply(revitTransform);
      }
    }

    var transformedGeometries = DB.DirectShape.CreateGeometryInstance(
      _converterSettings.Current.Document,
      target.atomicObject.applicationId ?? target.atomicObject.id.NotNull(),
      combinedTransform
    );

    result.SetShape(transformedGeometries);
    return result;
  }
}
