using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.DoubleNumerics;
using Speckle.Sdk;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class TransformConverterToHost : ITypedConverter<(Matrix4x4 matrix, string units), DB.Transform>
{
  private readonly ScalingServiceToHost _scalingService;

  public TransformConverterToHost(ScalingServiceToHost scalingService)
  {
    _scalingService = scalingService;
  }

  public DB.Transform Convert((Matrix4x4 matrix, string units) target)
  {
    var transform = DB.Transform.Identity;
    if (target.matrix.M44 == 0 || target.units is null) // TODO: check target.units nullability?
    {
      return transform;
    }

    var tX = _scalingService.ScaleToNative(target.matrix.M14 / target.matrix.M44, target.units);
    var tY = _scalingService.ScaleToNative(target.matrix.M24 / target.matrix.M44, target.units);
    var tZ = _scalingService.ScaleToNative(target.matrix.M34 / target.matrix.M44, target.units);
    var t = new DB.XYZ(tX, tY, tZ);

    // basis vectors
    DB.XYZ vX = new(target.matrix.M11, target.matrix.M21, target.matrix.M31);
    DB.XYZ vY = new(target.matrix.M12, target.matrix.M22, target.matrix.M32);
    DB.XYZ vZ = new(target.matrix.M13, target.matrix.M23, target.matrix.M33);

    // apply to new transform
    transform.Origin = t;
    transform.BasisX = vX;
    transform.BasisY = vY;
    transform.BasisZ = vZ;

    // TODO: check below needed?
    // // apply doc transform
    // var docTransform = GetDocReferencePointTransform(Doc);
    // var internalTransform = docTransform.Multiply(_transform);

    // Check if transform is conformal (no skew, uniform scale)
    try
    {
      double scale = transform.Scale; // Will throw if not conformal
    }
    catch (Exception)
    {
      throw new SpeckleException("Transform was not conformal. Skew, non uniform scale is currently not supported.");
    }

    return transform;
  }
}
