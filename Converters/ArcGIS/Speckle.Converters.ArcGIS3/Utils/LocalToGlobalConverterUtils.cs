using Speckle.Core.Logging;
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
    Base newObject = Core.Api.Operations.Deserialize(Core.Api.Operations.Serialize(atomicObject));

    if (newObject.TryGetDisplayValue() is IReadOnlyList<Base> listVals)
    {
      if (listVals.ToList().FindAll(x => x is not SOG.Mesh).Count == 0)
      {
        return TransformObjWithDisplayValues(newObject, matrix);
      }
      throw new SpeckleException(
        $"Display Values of types '{listVals.ToList().FindAll(x => x is not SOG.Mesh).Select(y => y.speckle_type).Distinct().ToList()}' for {newObject.speckle_type} are not supported for local to global coordinate transformation"
      );
    }
    // TODO: here or preferably in converters elegantly!
    // else if (newObject is SOG.Polyline polyline)
    // {
    //
    // }
    // else if (newObject is SOG.Line line)
    // {
    //
    // }
    // else if (newObject is SOG.Point point)
    // {
    //
    // }
    if (newObject is SOG.Mesh mesh)
    {
      return TransformMesh(mesh, matrix);
    }
    throw new SpeckleException(
      $"{newObject.speckle_type} is not supported for local to global coordinate transformation"
    );
  }

  private Base TransformObjWithDisplayValues(Base baseObj, List<Matrix4x4> matrix)
  {
    Base newObject = Core.Api.Operations.Deserialize(Core.Api.Operations.Serialize(baseObj));
    List<SOG.Mesh> newDisplayValue = new();

    var displayValue = newObject.TryGetDisplayValue();
    if (displayValue is null) // will not happen due to the check in "TransformObjects"
    {
      throw new SpeckleException($"{newObject.speckle_type} blocks contains no display value");
    }

    foreach (Base displayVal in displayValue)
    {
      if (displayVal is SOG.Mesh displayMesh)
      {
        var newMesh = TransformMesh(displayMesh, matrix);
        newDisplayValue.Add(newMesh);
      }
      else // will not happen due to the check in "TransformObjects"
      {
        throw new SpeckleException($"Blocks containing {baseObj.speckle_type} are not supported");
      }
    }

    newObject["displayValue"] = newDisplayValue;
    return newObject;
  }

  private SOG.Mesh TransformMesh(SOG.Mesh displayMesh, List<Matrix4x4> matrix)
  {
    List<List<double>> oldVertices = new();
    for (int i = 0; i < displayMesh.vertices.Count; i += 3)
    {
      List<double> group = new();
      for (int j = 0; j < 3 && i + j < displayMesh.vertices.Count; j++)
      {
        group.Add(displayMesh.vertices[i + j]);
      }
      oldVertices.Add(group);
    }

    List<double> newVertices = new();
    foreach (List<double> vertex in oldVertices)
    {
      var ptVector = new Vector3(vertex[0], vertex[1], vertex[2]);

      foreach (var matr in matrix)
      {
        ptVector = TransformPt(ptVector, matr);
      }
      newVertices.AddRange([ptVector.X, ptVector.Y, ptVector.Z]);
    }

    SOG.Mesh newMesh =
      new()
      {
        vertices = newVertices,
        faces = new List<int>(displayMesh.faces),
        colors = new List<int>(displayMesh.colors)
      };
    return newMesh;
  }
}
