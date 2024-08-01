using Objects;
using Speckle.Converters.Common;
using Speckle.Core.Models;
using Speckle.DoubleNumerics;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.ArcGIS3.Utils;

// POC: We could pass transformation matrices to converters by default and evaluate there instead as utils.
[GenerateAutoInterface]
public class LocalToGlobalConverterUtils : ILocalToGlobalConverterUtils
{
  private Vector3 TransformPt(Vector3 vector, Matrix4x4 matrix)
  {
    var divisor = matrix.M41 + matrix.M42 + matrix.M43 + matrix.M44;
    var x = (vector.X * matrix.M11 + vector.Y * matrix.M12 + vector.Z * matrix.M13 + matrix.M14) / divisor;
    var y = (vector.X * matrix.M21 + vector.Y * matrix.M22 + vector.Z * matrix.M23 + matrix.M24) / divisor;
    var z = (vector.X * matrix.M31 + vector.Y * matrix.M32 + vector.Z * matrix.M33 + matrix.M34) / divisor;

    return new Vector3(x, y, z);
  }

  // POC: This could move to converters instead handling all cases like this.
  public Base TransformObjects(Base atomicObject, List<Matrix4x4> matrix)
  {
    if (matrix.Count == 0)
    {
      return atomicObject;
    }

    List<Objects.Other.Transform> transforms = matrix.Select(x => new Objects.Other.Transform(x, "none")).ToList();

    if (atomicObject is ITransformable c)
    {
      foreach (var transform in transforms)
      {
        c.TransformTo(transform, out ITransformable newObj);
        c = newObj;
      }

      if (c is not Base)
      {
        throw new SpeckleConversionException("");
      }

      atomicObject = (Base)c;

      foreach (var prop in atomicObject.GetMembers(DynamicBaseMemberType.Dynamic))
      {
        atomicObject[prop.Key] = prop.Value;
      }

      return atomicObject;
    }

    throw new SpeckleConversionException(
      $"{atomicObject.speckle_type} is not supported for local to global coordinate transformation"
    );
  }
}
