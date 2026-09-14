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
/// A duct IS its location curve, so its centerline is free. A fitting is placed at a point and has none, which left a
/// gap in every run. Each connector says where the run meets the fitting, so the fitting ships ONE SEGMENT PER
/// CONNECTOR to its insertion point — what a single-line drawing draws, and the only shape that survives a tee, where
/// no single curve can express the branch.
/// </remarks>
public class MepCenterlineExtractor(
  ITypedConverter<XYZ, SOG.Point> pointConverter,
  IConverterSettingsStore<RevitConversionSettings> converterSettings
)
{
  /// <summary>
  /// The fitting's centerline branches, one per end connector, in Revit's order. Empty for anything that is not a
  /// connector-bearing family instance.
  /// </summary>
  /// <remarks>
  /// Points go through the same converter the location curves use, so scaling and the reference-point transform match
  /// the display meshes by construction — provided the caller is inside the owning document's settings push.
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

    // A placed fitting's insertion point IS the node its branches meet at; averaging the origins is the last resort
    // for a family reporting no location, and degrades to the chord rather than to nothing.
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
}
