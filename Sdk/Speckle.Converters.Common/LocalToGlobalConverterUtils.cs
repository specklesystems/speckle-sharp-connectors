using Speckle.DoubleNumerics;
using Speckle.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common;

// POC: We could pass transformation matrices to converters by default and evaluate there instead as utils.
public class LocalToGlobalConverterUtils
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
  public Base TransformObjects(Base atomicObject, IReadOnlyCollection<Matrix4x4> matrix)
  {
    if (matrix.Count == 0)
    {
      return atomicObject;
    }

    List<Speckle.Objects.Other.Transform> transforms = matrix
      .Select(x => new Speckle.Objects.Other.Transform() { matrix = x, units = Units.None })
      .ToList();

    if (atomicObject is ITransformable c)
    {
      //TODO TransformTo will be deprecated as it's dangerous and requires ID transposing which is wrong!
      //ID needs to be copied to the new instance
      string id = atomicObject.id.NotNull();
      foreach (var transform in transforms)
      {
        c.TransformTo(transform, out ITransformable newObj);
        c = newObj; // we need to keep the reference to the new object, as we're going to use it in the cache
      }

      if (c is not Base)
      {
        throw new ConversionException(
          $"Blocks transformation of type {atomicObject.speckle_type} did not return a valid object"
        );
      }

      atomicObject = (Base)c; // restore the id, as it's used in the cache
      atomicObject.id = id;

      // .TransformTo only transfers typed properties, we need to add back the dynamic ones:
      foreach (var prop in atomicObject.GetMembers(DynamicBaseMemberType.Dynamic))
      {
        if (prop.Value is not Base)
        {
          atomicObject[prop.Key] = prop.Value;
        }
        else
        {
          // TODO: add more granular warnings if needed that the property was skipped for blocks
        }
      }

      return atomicObject;
    }

    throw new ConversionException(
      $"{atomicObject.speckle_type} is not supported for local to global coordinate transformation"
    );
  }
}
