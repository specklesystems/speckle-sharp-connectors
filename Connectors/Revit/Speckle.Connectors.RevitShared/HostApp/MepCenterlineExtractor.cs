using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Derives the centerline of a point-placed MEP fitting [ENG-9510].
/// </summary>
/// <remarks>
/// <para>A duct IS its location curve, so its centerline is free. A fitting is placed at a point and has none, which
/// left a gap in every run. Two sources, in order:</para>
/// <para>(1) The path the family itself carries. Fitting families draw their single-line representation as model
/// curves — an elbow's is the true arc — and <c>get_Geometry</c> hands them back. Which <c>Options</c> return
/// them varies by category and family author, so the curves are only trusted when they tie every connector together;
/// stray symbolic lines and families without a path fall through.</para>
/// <para>(2) ONE STRAIGHT SEGMENT PER CONNECTOR to the insertion point — what a single-line drawing draws when the
/// family tells us nothing, and always available.</para>
/// </remarks>
public class MepCenterlineExtractor(
  ITypedConverter<XYZ, SOG.Point> pointConverter,
  ITypedConverter<Curve, ICurve> curveConverter,
  IConverterSettingsStore<RevitConversionSettings> converterSettings
)
{
  /// <summary>
  /// The fitting's centerline pieces: the family's own path curves when they connect its end connectors, else one
  /// straight branch per end connector in Revit's order. Empty for anything that is not a connector-bearing family
  /// instance.
  /// </summary>
  /// <remarks>
  /// Everything goes through the same converters the location curves use, so scaling and the reference-point
  /// transform match the display meshes by construction — provided the caller is inside the owning document's
  /// settings push.
  /// </remarks>
  public IReadOnlyList<Base> GetCenterlineBranches(Element element)
  {
    if (element is not FamilyInstance { MEPModel.ConnectorManager: { } connectorManager })
    {
      return [];
    }

    var origins = new List<XYZ>();
    foreach (Connector connector in connectorManager.Connectors)
    {
      // End is the atomic type a run's endpoint reports; this enum's composite members (Physical = End|Curve|Surface,
      // …) are query masks no connector ever equals. Testing for it positively also excludes the logical connectors
      // whose CoordinateSystem throws.
      if (connector.ConnectorType == ConnectorType.End && IsFlowDomain(connector.Domain))
      {
        // CoordinateSystem.Origin, not Connector.Origin: the latter is documented to throw for a connector belonging
        // to a family instance, i.e. every fitting. Both name the same point.
        origins.Add(connector.CoordinateSystem.Origin);
      }
    }

    if (origins.Count == 0)
    {
      return [];
    }

    return GetFamilyPath(element, origins) ?? GetConnectorBranches(element, origins);
  }

  // The family's own path curves, or null when no Options set yields curves that tie the connectors together.
  private List<Base>? GetFamilyPath(Element element, List<XYZ> origins)
  {
    // A cap has nothing to connect; its one branch is already exact.
    if (origins.Count < 2)
    {
      return null;
    }

    double tolerance = element.Document.Application.ShortCurveTolerance;
    foreach (Options options in PathGeometryOptions())
    {
      try
      {
        var curves = new List<Curve>();
        CollectCurves(element.get_Geometry(options), curves, tolerance);
        if (curves.Count is 0 or > MAX_PATH_CURVES)
        {
          continue;
        }

        List<Curve>? path = ConnectingCurves(curves, origins, tolerance);
        if (path is not null)
        {
          return path.Select(curve => (Base)curveConverter.Convert(curve)).ToList();
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // A view that can't drive geometry or a curve type the converter doesn't cover only rules out this attempt.
      }
    }

    return null;
  }

  // Cheapest and view-independent first. Coarse is where MEP families show their single-line path; some categories
  // ignore DetailLevel without a view (CNX-2735) and some only keep the path as non-visible geometry, hence the rest.
  // A view also brings its crop and visibility along — harmless here, a clipped path no longer reaches the connectors.
  private IEnumerable<Options> PathGeometryOptions()
  {
    yield return new Options { DetailLevel = ViewDetailLevel.Coarse };
    yield return new Options();
    yield return new Options { DetailLevel = ViewDetailLevel.Coarse, IncludeNonVisibleObjects = true };

    // null on linked documents
    if (converterSettings.Current.Document.ActiveView is { } activeView)
    {
      yield return new Options { View = activeView, IncludeNonVisibleObjects = true };
    }
  }

  private static void CollectCurves(GeometryElement? geometry, List<Curve> curves, double tolerance)
  {
    if (geometry is null)
    {
      return;
    }

    foreach (GeometryObject geometryObject in geometry)
    {
      switch (geometryObject)
      {
        // Instance geometry, not symbol geometry: it is already in the coordinates the connectors report.
        case GeometryInstance instance:
          CollectCurves(instance.GetInstanceGeometry(), curves, tolerance);
          break;
        case Curve { IsBound: true } curve
          when curve.Length > tolerance && !curves.Exists(c => IsSameCurve(c, curve, tolerance)):
          curves.Add(curve);
          break;
      }
    }
  }

  // Visible and non-visible copies of one curve would otherwise prop each other up in the dead-end pruning below.
  private static bool IsSameCurve(Curve a, Curve b, double tolerance)
  {
    XYZ a0 = a.GetEndPoint(0);
    XYZ a1 = a.GetEndPoint(1);
    XYZ b0 = b.GetEndPoint(0);
    XYZ b1 = b.GetEndPoint(1);
    bool sameEnds =
      (a0.DistanceTo(b0) < tolerance && a1.DistanceTo(b1) < tolerance)
      || (a0.DistanceTo(b1) < tolerance && a1.DistanceTo(b0) < tolerance);
    return sameEnds && a.Evaluate(0.5, true).DistanceTo(b.Evaluate(0.5, true)) < tolerance;
  }

  // Reduces whatever curves the family returned to the ones that form its path: reachable from the first connector,
  // with every dead end that does not finish on a connector trimmed away. Null unless what remains touches every
  // connector — that check is what makes a family's arbitrary curves safe to ship as a centerline.
  private static List<Curve>? ConnectingCurves(List<Curve> curves, List<XYZ> origins, double tolerance)
  {
    bool EndsAt(Curve curve, XYZ point) =>
      curve.GetEndPoint(0).DistanceTo(point) < tolerance || curve.GetEndPoint(1).DistanceTo(point) < tolerance;

    // On the other curve, not just at its ends: a tee's branch meets the through-run mid-span.
    bool EndLiesOn(Curve curve, Curve other) =>
      other.Distance(curve.GetEndPoint(0)) < tolerance || other.Distance(curve.GetEndPoint(1)) < tolerance;

    bool Touch(Curve a, Curve b) => EndLiesOn(a, b) || EndLiesOn(b, a);

    var path = curves.Where(curve => EndsAt(curve, origins[0])).ToList();
    var pending = new Queue<Curve>(path);
    while (pending.Count > 0)
    {
      Curve reached = pending.Dequeue();
      foreach (Curve curve in curves)
      {
        if (!path.Contains(curve) && Touch(reached, curve))
        {
          path.Add(curve);
          pending.Enqueue(curve);
        }
      }
    }

    bool IsAnchored(Curve curve, XYZ end) =>
      origins.Exists(origin => origin.DistanceTo(end) < tolerance)
      || path.Exists(other => !ReferenceEquals(other, curve) && other.Distance(end) < tolerance);

    while (
      path.Find(curve => !IsAnchored(curve, curve.GetEndPoint(0)) || !IsAnchored(curve, curve.GetEndPoint(1)))
        is { } deadEnd
    )
    {
      path.Remove(deadEnd);
    }

    return origins.TrueForAll(origin => path.Exists(curve => EndsAt(curve, origin))) ? path : null;
  }

  private List<Base> GetConnectorBranches(Element element, List<XYZ> origins)
  {
    // A placed fitting's insertion point IS the node its branches meet at; averaging the origins is the last resort
    // for a family reporting no location, and degrades to the chord rather than to nothing.
    XYZ node = element.Location is LocationPoint locationPoint
      ? locationPoint.Point
      : origins.Aggregate(XYZ.Zero, (sum, origin) => sum.Add(origin)).Divide(origins.Count);

    string units = converterSettings.Current.SpeckleUnits;
    SOG.Point nodePoint = pointConverter.Convert(node);
    var branches = new List<Base>(origins.Count);
    foreach (XYZ origin in origins)
    {
      var branch = new SOG.Line
      {
        start = pointConverter.Convert(origin),
        end = nodePoint,
        units = units,
      };
      // A connector sitting on the node would only put a degenerate curve on the port.
      if (branch.length > BRANCH_TOLERANCE)
      {
        branches.Add(branch);
      }
    }

    return branches;
  }

  /// <summary>Domains where a connector marks a flow run; electrical and analytical ones carry no run geometry.</summary>
  private static bool IsFlowDomain(Domain domain) =>
    domain is Domain.DomainHvac or Domain.DomainPiping or Domain.DomainCableTrayConduit;

  // Shortest branch worth shipping, in the send's own units.
  private const double BRANCH_TOLERANCE = 1e-6;

  // A path is a handful of curves; a family returning more than this is drawing something else.
  private const int MAX_PATH_CURVES = 64;
}
