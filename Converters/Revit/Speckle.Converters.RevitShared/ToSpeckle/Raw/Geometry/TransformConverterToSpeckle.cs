using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.ToSpeckle.Raw.Geometry;

public class TransformConverterToSpeckle : ITypedConverter<(DB.Transform transform, string units), Matrix4x4>
{
  private readonly IScalingServiceToSpeckle _scalingService;

  public TransformConverterToSpeckle(IScalingServiceToSpeckle scalingService)
  {
    _scalingService = scalingService;
  }

  public Matrix4x4 Convert((DB.Transform transform, string units) target)
  {
    var tX = _scalingService.ScaleLength(target.transform.Origin.X);
    var tY = _scalingService.ScaleLength(target.transform.Origin.Y);
    var tZ = _scalingService.ScaleLength(target.transform.Origin.Z);

    return new Matrix4x4(
      target.transform.BasisX.X,
      target.transform.BasisY.X,
      target.transform.BasisZ.X,
      tX,
      target.transform.BasisX.Y,
      target.transform.BasisY.Y,
      target.transform.BasisZ.Y,
      tY,
      target.transform.BasisX.Z,
      target.transform.BasisY.Z,
      target.transform.BasisZ.Z,
      tZ,
      0,
      0,
      0,
      1
    );
  }
}
