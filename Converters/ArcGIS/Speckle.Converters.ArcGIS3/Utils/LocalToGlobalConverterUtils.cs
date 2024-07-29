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
    Base newObject = Core.Api.Operations.Deserialize(Core.Api.Operations.Serialize(atomicObject));

    if (newObject.TryGetDisplayValue() is IReadOnlyList<Base> listVals)
    {
      if (listVals.ToList().FindAll(x => x is not SOG.Mesh).Count == 0)
      {
        return TransformObjWithDisplayValues(newObject, matrix);
      }
      throw new SpeckleConversionException(
        $"Display Values of types '{listVals.ToList().FindAll(x => x is not SOG.Mesh).Select(y => y.speckle_type).Distinct().ToList()}' for {newObject.speckle_type} are not supported for local to global coordinate transformation"
      );
    }

    if (newObject is SOG.Point point1)
    {
      Base baseObj = Core.Api.Operations.Deserialize(Core.Api.Operations.Serialize(point1));
      if (baseObj is SOG.Point point)
      {
        return TransformPoint(point, matrix);
      }
      throw new SpeckleConversionException(
        $"Transformation of {newObject.speckle_type} from local to global coordinates failed"
      );
    }

    if (newObject is ICurve icurve1)
    {
      Base baseObj = Core.Api.Operations.Deserialize(Core.Api.Operations.Serialize((Base)icurve1));
      if (baseObj is ICurve icurve)
      {
        return TransformICurve((Base)icurve, matrix);
      }
      throw new SpeckleConversionException(
        $"Transformation of {newObject.speckle_type} from local to global coordinates failed"
      );
    }
    if (newObject is SOG.Mesh mesh1)
    {
      Base baseObj = Core.Api.Operations.Deserialize(Core.Api.Operations.Serialize(mesh1));
      if (baseObj is SOG.Mesh mesh)
      {
        return TransformMesh(mesh, matrix);
      }
      throw new SpeckleConversionException(
        $"Transformation of {newObject.speckle_type} from local to global coordinates failed"
      );
    }
    throw new SpeckleConversionException(
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
      throw new SpeckleConversionException($"{newObject.speckle_type} blocks contains no display value");
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
        throw new SpeckleConversionException($"Blocks containing {baseObj.speckle_type} are not supported");
      }
    }

    newObject["displayValue"] = newDisplayValue;
    return newObject;
  }

  private SOG.Point TransformPoint(SOG.Point point, List<Matrix4x4> matrix)
  {
    // all geometry transforms will be done through this function
    var ptVector = new Vector3(point.x, point.y, point.z);

    foreach (var matr in matrix)
    {
      ptVector = TransformPt(ptVector, matr);
    }

    // only modify coordinates to not lose any extra properties
    point.x = ptVector.X;
    point.y = ptVector.Y;
    point.z = ptVector.Z;
    return point;
  }

  private Base TransformICurve(Base newObject, List<Matrix4x4> matrix)
  {
    if (newObject is SOG.Line line)
    {
      var startPt = TransformPoint(line.start, matrix);
      var endPt = TransformPoint(line.end, matrix);
      line.start = startPt;
      line.end = endPt;
      return line;
    }
    if (newObject is SOG.Polyline polyline)
    {
      return TransformPolyline(polyline, matrix);
    }
    if (newObject is SOG.Curve curve)
    {
      return TransformPolyline(curve.displayValue, matrix);
    }
    if (newObject is SOG.Arc arc)
    {
      var newOrigin = TransformPoint(arc.plane.origin, matrix);
      var startPt = TransformPoint(arc.startPoint, matrix);
      var midPt = TransformPoint(arc.midPoint, matrix);
      var endPt = TransformPoint(arc.endPoint, matrix);
      arc.plane.origin = newOrigin;
      arc.startPoint = startPt;
      arc.midPoint = midPt;
      arc.endPoint = endPt;
      return arc;
    }
    if (newObject is SOG.Circle circle)
    {
      var newOrigin = TransformPoint(circle.plane.origin, matrix);
      circle.plane.origin = newOrigin;
      return circle;
    }
    if (newObject is SOG.Ellipse ellipse)
    {
      var newOrigin = TransformPoint(ellipse.plane.origin, matrix);
      ellipse.plane.origin = newOrigin;
      return ellipse;
    }
    if (newObject is SOG.Polycurve polycurve)
    {
      List<ICurve> newSegments = new();
      foreach (var segment in polycurve.segments)
      {
        // need to hack again, otherwise parent Polycurve will be getting transformed
        Base newSegment = Core.Api.Operations.Deserialize(Core.Api.Operations.Serialize((Base)segment));
        newSegments.Add((ICurve)TransformICurve(newSegment, matrix));
      }

      polycurve.segments = newSegments;
      return polycurve;
    }

    throw new SpeckleConversionException(
      $"Transformation of {newObject.speckle_type} from local to global coordinates failed"
    );
  }

  private SOG.Polyline TransformPolyline(SOG.Polyline polyline, List<Matrix4x4> matrix)
  {
    List<double> newCoords = new();
    foreach (var pt in polyline.GetPoints())
    {
      var newPt = TransformPoint(pt, matrix);
      newCoords.AddRange([newPt.x, newPt.y, newPt.z]);
    }

    polyline.value = newCoords;
    return polyline;
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
      SOG.Point newPt = TransformPoint(new SOG.Point(vertex[0], vertex[1], vertex[2]), matrix);
      newVertices.AddRange([newPt.x, newPt.y, newPt.z]);
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
