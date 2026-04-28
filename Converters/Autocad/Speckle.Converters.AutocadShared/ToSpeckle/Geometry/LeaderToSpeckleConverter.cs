using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Geometry;

[NameAndRankValue(typeof(ADB.Leader), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class LeaderToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private const double ARROW_WIDTH_FACTOR = 0.35;

  private readonly ITypedConverter<List<double>, SOG.Polyline> _polylineConverter;

  public LeaderToSpeckleConverter(ITypedConverter<List<double>, SOG.Polyline> polylineConverter)
  {
    _polylineConverter = polylineConverter;
  }

  public Base Convert(object target) => Convert((ADB.Leader)target);

  public SOG.Polyline Convert(ADB.Leader target)
  {
    List<double> value = new();

    if (target.HasArrowHead && target.NumVertices > 1)
    {
      AppendArrowhead(target, value);
    }

    for (int i = 0; i < target.NumVertices; i++)
    {
      AppendPoint(value, target.VertexAt(i));
    }

    SOG.Polyline polyline = _polylineConverter.Convert(value);
    polyline.closed = false;
    return polyline;
  }

  private static void AppendArrowhead(ADB.Leader leader, List<double> value)
  {
    double arrowSize = leader.Dimasz;
    if (leader.Dimscale > 0)
    {
      arrowSize *= leader.Dimscale;
    }

    if (arrowSize <= 0)
    {
      return;
    }

    AG.Point3d head = leader.VertexAt(0);
    AG.Point3d next = leader.VertexAt(1);
    AG.Vector3d leaderDirection = next - head;
    if (leaderDirection.IsZeroLength())
    {
      return;
    }

    AG.Vector3d backDirection = leaderDirection.GetNormal();
    AG.Vector3d sideDirection = leader.Normal.CrossProduct(backDirection);
    if (sideDirection.IsZeroLength())
    {
      return;
    }

    AG.Point3d arrowBase = head + backDirection * arrowSize;
    AG.Vector3d halfWidth = sideDirection.GetNormal() * arrowSize * ARROW_WIDTH_FACTOR;

    AppendPoint(value, arrowBase + halfWidth);
    AppendPoint(value, head);
    AppendPoint(value, arrowBase - halfWidth);
    AppendPoint(value, head);
  }

  private static void AppendPoint(List<double> value, AG.Point3d point)
  {
    value.Add(point.X);
    value.Add(point.Y);
    value.Add(point.Z);
  }
}
