using System.Collections;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.RevitShared;

public class RevitRootToHostConverter : IRootToHostConverter
{
  private readonly IConverterResolver<IToHostTopLevelConverter> _converterResolver;
  private readonly ITypedConverter<ICurve, DB.CurveArray> _curveConverter;
  private readonly ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> _meshConverter;

  public RevitRootToHostConverter(
    IConverterResolver<IToHostTopLevelConverter> converterResolver,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter,
    ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> meshConverter
  )
  {
    _converterResolver = converterResolver;
    _curveConverter = curveConverter;
    _meshConverter = meshConverter;
  }

  public object Convert(Base target)
  {
    List<DB.GeometryElement> geometryElements = new();
    switch (target)
    {
      case ICurve curve:
        var curves = _curveConverter.Convert(curve).Cast<DB.GeometryElement>(); // TODO: check if casting is happening correctly
        geometryElements.AddRange(curves);
        break;
      case SOG.Mesh mesh:
        var meshes = _meshConverter.Convert(mesh).Cast<DB.GeometryElement>();
        geometryElements.AddRange(meshes); // TODO: check if casting is happening correctly
        break;
      default:
        FallbackToDisplayValue(target);
        break;
    }

    var objectConverter = _converterResolver.GetConversionForType(target.GetType());

    if (objectConverter == null)
    {
      throw new SpeckleConversionException($"No conversion found for {target.GetType().Name}");
    }

    return objectConverter.Convert(target)
      ?? throw new SpeckleConversionException($"Conversion of object with type {target.GetType()} returned null");
  }

  private List<DB.GeometryElement> FallbackToDisplayValue(Base target)
  {
    // TODO
    var displayValue = target.TryGetDisplayValue<Base>();
    if (displayValue is IList && !displayValue.Any())
    {
      throw new NotSupportedException($"No display value found for {target.speckle_type}");
    }

    // TODO
  }
}
