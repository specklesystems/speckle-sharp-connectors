using Objects;
using Speckle.Converters.Common;
using Speckle.Core.Models;
using Speckle.Core.Models.Extensions;
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

    // This is a temp hack. We would add transformations to conversions later instead try to copy objects like this.
    // Jedd also has opinions on this.
    // Base newObject = Core.Api.Operations.Deserialize(Core.Api.Operations.Serialize(atomicObject));

    List<Objects.Other.Transform> transforms = new() { };
    transforms.AddRange(matrix.Select(x => new Objects.Other.Transform(x, "none")).ToList());

    if (atomicObject is ITransformable c && atomicObject is not SOG.Brep)
    {
      foreach (var transform in transforms)
      {
        c.TransformTo(transform, out ITransformable newObj);
        c = newObj;
      }

      atomicObject = (Base)c;
      foreach (var prop in atomicObject.GetMembers(DynamicBaseMemberType.Dynamic))
      {
        atomicObject[prop.Key] = prop.Value;
      }

      return atomicObject;
    }

    if (atomicObject.TryGetDisplayValue() is IReadOnlyList<Base> listVals)
    {
      if (listVals.ToList().FindAll(x => x is not ITransformable).Count == 0)
      {
        return TransformObjWithDisplayValues(atomicObject, transforms);
      }
      throw new SpeckleConversionException(
        $"Display Values of types '{listVals.ToList().FindAll(x => x is not ITransformable).Select(y => y.speckle_type).Distinct().ToList()}' for {atomicObject.speckle_type} are not supported for local to global coordinate transformation"
      );
    }
    throw new SpeckleConversionException(
      $"{atomicObject.speckle_type} is not supported for local to global coordinate transformation"
    );
  }

  private Base TransformObjWithDisplayValues(Base atomicObject, List<Objects.Other.Transform> transforms)
  {
    // for all objects that are not transformable, but contain displayValue
    List<Base> newDisplayValues = new();

    var displayValue = atomicObject.TryGetDisplayValue();
    if (displayValue is null) // will not happen due to the check in "TransformObjects"
    {
      throw new SpeckleConversionException($"{atomicObject.speckle_type} blocks contains no display value");
    }

    foreach (Base displayVal in displayValue)
    {
      if (displayVal is ITransformable c)
      {
        foreach (var transform in transforms)
        {
          c.TransformTo(transform, out ITransformable newObj);
          c = newObj;
        }
        newDisplayValues.Add((Base)c);
      }
      else // will not happen due to the check in "TransformObjects"
      {
        throw new SpeckleConversionException(
          $"Blocks containing {displayVal.speckle_type} as displayValue are not supported"
        );
      }
    }
    // copy the original object and assign new displayValue - hacky
    Base newObject = Core.Api.Operations.Deserialize(Core.Api.Operations.Serialize(atomicObject));
    if (newObject is SOG.Brep)
    {
      newObject["displayValue"] = newDisplayValues.Select(x => (SOG.Mesh)x).ToList();
    }
    else
    {
      newObject["displayValue"] = newDisplayValues;
    }
    return newObject;
  }
}
