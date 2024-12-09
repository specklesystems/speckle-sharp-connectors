using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

[NameAndRankValue(nameof(TSM.BooleanPart), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class OpeningToSpeckleConverter : ITypedConverter<TSM.BooleanPart, IEnumerable<Base>>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.LineSegment, SOG.Line> _lineConverter;

  public OpeningToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.LineSegment, SOG.Line> lineConverter
  )
  {
    _settingsStore = settingsStore;
    _lineConverter = lineConverter;
  }

  private (double minZ, double maxZ) GetParentZBounds(TSM.ModelObject parent)
  {
    TSM.Solid? solid = null;

    // detect if the solid is a part or cut
    if (parent is TSM.Part part)
    {
      solid = part.GetSolid();
    }
    else if (parent is TSM.BooleanPart boolPart)
    {
      solid = boolPart.OperativePart?.GetSolid();
    }

    if (solid == null)
    {
      return (0, 0);
    }

    double minZ = double.MaxValue;
    double maxZ = double.MinValue;

    var faceEnum = solid.GetFaceEnumerator();
    while (faceEnum.MoveNext())
    {
      var face = faceEnum.Current;
      if (face == null)
      {
        continue;
      }

      var loopEnum = face.GetLoopEnumerator();
      while (loopEnum.MoveNext())
      {
        var loop = loopEnum.Current;
        if (loop == null)
        {
          continue;
        }

        var vertexEnum = loop.GetVertexEnumerator();
        while (vertexEnum.MoveNext())
        {
          var vertex = vertexEnum.Current;
          if (vertex == null)
          {
            continue;
          }

          minZ = Math.Min(minZ, vertex.Z);
          maxZ = Math.Max(maxZ, vertex.Z);
        }
      }
    }

    return (minZ, maxZ);
  }

  private TG.Point ClampPointToZBounds(TG.Point point, double minZ, double maxZ)
  {
    return new TG.Point(point.X, point.Y, Math.Max(minZ, Math.Min(maxZ, point.Z)));
  }

  public IEnumerable<Base> Convert(TSM.BooleanPart target)
  {
    // skip if this is not a cut operation
    // since there are some boolean parts which is not a cut (merge operation for example)
    if (target.Type != TSM.BooleanPart.BooleanTypeEnum.BOOLEAN_CUT)
    {
      yield break;
    }

    var operativePart = target.OperativePart;
    var fatherObject = target.Father;
    if (operativePart == null || fatherObject == null)
    {
      yield break;
    }

    // when the cut operation is a part cut, it was adding lines for whole part
    // therefore we're restricting the Z coordinate to the plane
    // get the Z bounds of the parent object
    var (minZ, maxZ) = GetParentZBounds(fatherObject);

    // Ggt the solid of the operative part
    var solid = operativePart.GetSolid();
    if (solid == null)
    {
      yield break;
    }

    // get the face enumerator to traverse the faces of the solid
    var faceEnum = solid.GetFaceEnumerator();
    while (faceEnum.MoveNext())
    {
      var face = faceEnum.Current;
      if (face == null)
      {
        continue;
      }

      var loopEnum = face.GetLoopEnumerator();
      while (loopEnum.MoveNext())
      {
        var loop = loopEnum.Current;
        if (loop == null)
        {
          continue;
        }

        var vertices = new List<TG.Point>();
        var vertexEnum = loop.GetVertexEnumerator();
        while (vertexEnum.MoveNext())
        {
          if (vertexEnum.Current != null)
          {
            // restrict the Z value within parent bounds
            vertices.Add(ClampPointToZBounds(vertexEnum.Current, minZ, maxZ));
          }
        }

        for (int i = 0; i < vertices.Count; i++)
        {
          var startPoint = vertices[i];
          var endPoint = vertices[(i + 1) % vertices.Count];

          var lineSegment = new TG.LineSegment(startPoint, endPoint);
          var speckleLine = _lineConverter.Convert(lineSegment);
          speckleLine["type"] = "Opening";

          yield return speckleLine;
        }
      }
    }
  }
}
