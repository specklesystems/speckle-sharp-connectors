using Speckle.Converters.MicroStation.Services;
using Speckle.Objects.Annotation;

namespace Speckle.Converters.MicroStation.ToSpeckle;

/// <summary>
/// TextElement / TextNodeElement → Speckle <see cref="Text"/>. Reads each text part's
/// <see cref="DPN.TextBlock"/> (string + user origin + orientation) via the TextQuery surface.
/// Height comes from the block's nominal range (the run-level height isn't exposed on the block).
/// </summary>
public class TextConverter(GeometryMapper mapper)
{
  public List<Text> Convert(MgdElements.TextQuery textQuery)
  {
    var result = new List<Text>();
    DPN.TextPartIdCollection? partIds = textQuery.GetTextPartIds(new DPN.TextQueryOptions());
    if (partIds == null)
    {
      return result;
    }

    foreach (DPN.TextPartId? partId in partIds)
    {
      if (partId == null)
      {
        continue;
      }
      DPN.TextBlock? block = textQuery.GetTextPart(partId);
      if (block == null || block.IsEmpty)
      {
        continue;
      }

      string value = block.ToString() ?? "";
      if (value.Length == 0)
      {
        continue;
      }

      BG.DPoint3d origin = block.GetUserOrigin();
      BG.DMatrix3d orientation = block.GetOrientation();
      BG.DRange3d range = block.GetNominalRange();
      double heightUor = Math.Max(range.High.Y - range.Low.Y, 0);
      double scale = 1.0 / mapper.UorPerMasterForTolerances();

      BG.DVector3d xdir = orientation.Multiply(new BG.DVector3d { X = 1 });
      BG.DVector3d ydir = orientation.Multiply(new BG.DVector3d { Y = 1 });

      result.Add(
        new Text
        {
          value = value,
          height = heightUor * scale,
          maxWidth = null,
          plane = new SOG.Plane
          {
            origin = mapper.MapPoint(origin),
            normal = mapper.MapDirection(Cross(xdir, ydir)),
            xdir = mapper.MapDirection(xdir),
            ydir = mapper.MapDirection(ydir),
            units = mapper.Units,
          },
          screenOriented = false,
          units = mapper.Units,
        }
      );
    }
    return result;
  }

  private static BG.DVector3d Cross(BG.DVector3d a, BG.DVector3d b) =>
    new()
    {
      X = a.Y * b.Z - a.Z * b.Y,
      Y = a.Z * b.X - a.X * b.Z,
      Z = a.X * b.Y - a.Y * b.X,
    };
}
