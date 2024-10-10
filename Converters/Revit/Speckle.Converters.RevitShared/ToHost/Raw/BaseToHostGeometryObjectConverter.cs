using System.Collections;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class BaseToHostGeometryObjectConverter : ITypedConverter<Base, List<DB.GeometryObject>>
{
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointConverter;
  private readonly ITypedConverter<ICurve, DB.CurveArray> _curveConverter;
  private readonly ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> _meshConverter;

  public BaseToHostGeometryObjectConverter(
    ITypedConverter<SOG.Point, DB.XYZ> pointConverter,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter,
    ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> meshConverter
  )
  {
    _pointConverter = pointConverter;
    _curveConverter = curveConverter;
    _meshConverter = meshConverter;
  }

  public List<DB.GeometryObject> Convert(Base target)
  {
    List<DB.GeometryObject> result = new();

    switch (target)
    {
      case SOG.Point point:
        var xyz = _pointConverter.Convert(point);
        result.Add(DB.Point.Create(xyz));
        break;
      case ICurve curve:
        var curves = _curveConverter.Convert(curve).Cast<DB.GeometryObject>();
        result.AddRange(curves);
        break;
      case SOG.Mesh mesh:
        var meshes = _meshConverter.Convert(mesh).Cast<DB.GeometryObject>();
        result.AddRange(meshes);
        break;
      default:
        var displayValue = target.TryGetDisplayValue<Base>();
        if ((displayValue is IList && !displayValue.Any()) || displayValue is null)
        {
          throw new ValidationException($"No display value found for {target.speckle_type}");
        }

        foreach (var display in displayValue)
        {
          result.AddRange(Convert(display));
        }

        break;
    }

    if (result.Count == 0)
    {
      throw new ConversionException($"No objects could be converted for {target.speckle_type}.");
    }

    return result;
  }
}
