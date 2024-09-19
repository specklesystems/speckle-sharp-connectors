using System.Collections;
using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.RevitShared;

public class RevitRootToHostConverter : IRootToHostConverter
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly IConverterResolver<IToHostTopLevelConverter> _converterResolver;
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointConverter;
  private readonly ITypedConverter<ICurve, DB.CurveArray> _curveConverter;
  private readonly ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> _meshConverter;

  public RevitRootToHostConverter(
    IConverterResolver<IToHostTopLevelConverter> converterResolver,
    ITypedConverter<SOG.Point, DB.XYZ> pointConverter,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter,
    ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> meshConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _converterResolver = converterResolver;
    _pointConverter = pointConverter;
    _curveConverter = curveConverter;
    _meshConverter = meshConverter;
    _converterSettings = converterSettings;
  }

  public object Convert(Base target)
  {
    List<DB.GeometryObject> geometryObjects = new();

    switch (target)
    {
      case SOG.Point point:
        var xyz = _pointConverter.Convert(point);
        geometryObjects.Add(DB.Point.Create(xyz));
        break;
      case ICurve curve:
        var curves = _curveConverter.Convert(curve).Cast<DB.GeometryObject>();
        geometryObjects.AddRange(curves);
        break;
      case SOG.Mesh mesh:
        var meshes = _meshConverter.Convert(mesh).Cast<DB.GeometryObject>();
        geometryObjects.AddRange(meshes);
        break;
      default:
        geometryObjects.AddRange(FallbackToDisplayValue(target));
        break;
    }

    if (geometryObjects.Count == 0)
    {
      throw new SpeckleConversionException($"No supported conversion for {target.speckle_type} found.");
    }

    var category = DB.BuiltInCategory.OST_GenericModel;
    if (target["category"] is string categoryString)
    {
      var res = Enum.TryParse($"OST_{categoryString}", out DB.BuiltInCategory cat);
      if (res)
      {
        var c = Category.GetCategory(_converterSettings.Current.Document, cat);
        if (c is not null && DirectShape.IsValidCategoryId(c.Id, _converterSettings.Current.Document))
        {
          category = cat;
        }
      }
    }

    var ds = DB.DirectShape.CreateElement(_converterSettings.Current.Document, new DB.ElementId(category));
    ds.SetShape(geometryObjects);

    return ds;
  }

  private List<DB.GeometryObject> FallbackToDisplayValue(Base target)
  {
    var displayValue = target.TryGetDisplayValue<Base>();
    if ((displayValue is IList && !displayValue.Any()) || displayValue is null)
    {
      throw new NotSupportedException($"No display value found for {target.speckle_type}");
    }

    List<DB.GeometryObject> geometryObjects = new();
    foreach (var baseObject in displayValue)
    {
      switch (baseObject)
      {
        case SOG.Point point:
          var xyz = _pointConverter.Convert(point);
          geometryObjects.Add(DB.Point.Create(xyz));
          break;
        case ICurve curve:
          var curves = _curveConverter.Convert(curve).Cast<DB.GeometryObject>();
          geometryObjects.AddRange(curves);
          break;
        case SOG.Mesh mesh:
          var meshes = _meshConverter.Convert(mesh).Cast<DB.GeometryObject>();
          geometryObjects.AddRange(meshes);
          break;
      }
    }
    return geometryObjects;
  }
}
