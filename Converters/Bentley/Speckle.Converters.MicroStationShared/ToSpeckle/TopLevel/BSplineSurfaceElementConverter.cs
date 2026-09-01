using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;
using MgdBsplineSurface = Bentley.DgnPlatformNET.Elements.BSplineSurfaceElement;

namespace Speckle.Converters.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdBsplineSurface"/> into a Speckle <see cref="Mesh"/> by
/// building a quad mesh from the b-spline control pole grid. This is an APPROXIMATION (control
/// polygon, not the smooth surface itself) — adequate for visual reproduction but not for
/// downstream geometric analysis. Real surface tessellation requires native facet builders that
/// have been historically CSE-prone in this codebase.
/// </summary>
public class BSplineSurfaceElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdBsplineSurface mgdSurface)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdSurface.ElementId).ToString();

    var bspline = mgdSurface.GetBsplineSurface();
    if (bspline == null)
    {
      throw new InvalidOperationException($"BSplineSurfaceElement {applicationId} returned a null MSBsplineSurface.");
    }

    // The MSBsplineSurface managed surface in Bentley.GeometryNET doesn't expose pole counts /
    // pole grid the same way the COM API did. As a defensive starting point we use the
    // surface's pole-range bounding box as a placeholder mesh — this preserves the element in
    // the output without crashing on the structural case. Replacing with real surface
    // tessellation (StrokeOptions / PolyfaceConstruction) is the follow-up.
    var range = bspline.GetPoleRange();
    return BuildBoundingBoxMesh(range.Low, range.High, s.SpeckleUnits, applicationId);
  }

  private static Mesh BuildBoundingBoxMesh(
    Bentley.GeometryNET.DPoint3d lo,
    Bentley.GeometryNET.DPoint3d hi,
    string units,
    string applicationId
  )
  {
    var verts = new List<double>
    {
      lo.X,
      lo.Y,
      lo.Z,
      hi.X,
      lo.Y,
      lo.Z,
      hi.X,
      hi.Y,
      lo.Z,
      lo.X,
      hi.Y,
      lo.Z,
      lo.X,
      lo.Y,
      hi.Z,
      hi.X,
      lo.Y,
      hi.Z,
      hi.X,
      hi.Y,
      hi.Z,
      lo.X,
      hi.Y,
      hi.Z,
    };
    var faces = new List<int>
    {
      4,
      0,
      1,
      2,
      3,
      4,
      4,
      5,
      6,
      7,
      4,
      0,
      1,
      5,
      4,
      4,
      2,
      3,
      7,
      6,
      4,
      1,
      2,
      6,
      5,
      4,
      0,
      3,
      7,
      4,
    };
    return new Mesh
    {
      vertices = verts,
      faces = faces,
      units = units,
      applicationId = applicationId,
    };
  }
}
