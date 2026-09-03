using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Derives the centerline of a point-placed MEP fitting from its connectors [ENG-9510].
/// </summary>
/// <remarks>
/// <para>A duct or pipe IS its location curve, so its centerline is free. A fitting — elbow, tee, cross, transition —
/// is placed at a point and has no location curve at all, which left a gap in every run downstream. The geometry that
/// closes the gap is still authored, just not as a curve: each connector reports where the run enters or leaves the
/// fitting, and the fitting's insertion point is the node those branches meet at.</para>
/// <para><b>One segment per connector</b>, rather than one curve across the fitting. That is what a single-line
/// drawing draws, and it is the only shape that stays correct for a tee or a cross — no single curve can express a
/// branch. Consumers weld duct and fitting segments into continuous runs (Join Curves in Grasshopper).</para>
/// <para>Its own class rather than another method on the send builder: this is a self-contained host-API read, it is
/// the same shape as <see cref="ElementUnpacker"/> and friends, and folding the connector vocabulary into the builder
/// pushed that class past its class-coupling budget.</para>
/// </remarks>
public class MepCenterlineExtractor(
  ITypedConverter<XYZ, SOG.Point> pointConverter,
  IConverterSettingsStore<RevitConversionSettings> converterSettings
)
{
  /// <summary>
  /// The fitting's centerline branches, one per flow connector, ordered as Revit reports them. Empty for anything
  /// that is not a connector-bearing family instance, and for a fitting whose connectors cannot be read.
  /// </summary>
  /// <remarks>
  /// Points go through the same <see cref="ITypedConverter{XYZ, Point}"/> the location curves use, so scaling and the
  /// reference-point transform match the display meshes by construction — including in a linked model, provided the
  /// caller is inside that document's converter-settings push.
  /// </remarks>
  public IReadOnlyList<SOG.Line> GetCenterlineBranches(Element element)
  {
    if (element is not FamilyInstance { MEPModel.ConnectorManager: { } connectorManager })
    {
      return [];
    }

    var origins = new List<XYZ>();
    foreach (Connector connector in connectorManager.Connectors)
    {
      // A logical connector carries no geometry at all (CoordinateSystem throws on one), and a non-flow domain
      // (electrical, structural analytical) is not a run whose centerline anyone asked for.
      if (connector.ConnectorType == ConnectorType.Logical || !IsFlowDomain(connector.Domain))
      {
        continue;
      }
      // CoordinateSystem.Origin, NOT Connector.Origin: the latter is documented to throw for a connector that is
      // part of a family instance, which is every fitting. Both name the same point.
      origins.Add(connector.CoordinateSystem.Origin);
    }

    if (origins.Count == 0)
    {
      return [];
    }

    // The node the branches meet at. A placed fitting's insertion point IS that node; averaging the connector
    // origins is the last resort for a family that reports no location, and degrades to the chord rather than to
    // nothing.
    XYZ node = element.Location is LocationPoint locationPoint
      ? locationPoint.Point
      : origins.Aggregate(XYZ.Zero, (sum, origin) => sum.Add(origin)).Divide(origins.Count);

    string units = converterSettings.Current.SpeckleUnits;
    SOG.Point nodePoint = pointConverter.Convert(node);
    var branches = new List<SOG.Line>(origins.Count);
    foreach (XYZ origin in origins)
    {
      var branch = new SOG.Line
      {
        start = pointConverter.Convert(origin),
        end = nodePoint,
        units = units,
      };
      // A connector sitting on the node contributes no branch — emitting it would put a degenerate, zero-length
      // curve on the port for every consumer to filter out.
      if (branch.length > BRANCH_TOLERANCE)
      {
        branches.Add(branch);
      }
    }

    return branches;
  }

  // The domains where a connector marks a flow run. Electrical and structural-analytical connectors are a different
  // kind of statement and carry no run geometry.
  private static bool IsFlowDomain(Domain domain) =>
    domain is Domain.DomainHvac or Domain.DomainPiping or Domain.DomainCableTrayConduit;

  // The shortest branch worth shipping, in the send's own units — small enough that a real fitting branch never trips
  // it, large enough to drop a connector that coincides with the insertion point.
  private const double BRANCH_TOLERANCE = 1e-6;
}
