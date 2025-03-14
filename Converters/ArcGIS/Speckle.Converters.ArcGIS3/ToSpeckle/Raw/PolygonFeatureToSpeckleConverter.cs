using Speckle.Common.MeshTriangulation;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.DoubleNumerics;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

/// <summary>
/// Converts a Polygon feature to a list of polylines from the polygon boundary and inner loops.
/// This is a placeholder conversion since we don't have a polygon class or meshing strategy for interior loops yet.
/// </summary>
public class PolygonFeatureToSpeckleConverter : ITypedConverter<ACG.Polygon, IReadOnlyList<SOG.Region>>
{
  private readonly ITypedConverter<ACG.ReadOnlySegmentCollection, SOG.Polyline> _segmentConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public PolygonFeatureToSpeckleConverter(
    ITypedConverter<ACG.ReadOnlySegmentCollection, SOG.Polyline> segmentConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _segmentConverter = segmentConverter;
    _settingsStore = settingsStore;
  }

  public IReadOnlyList<SOG.Region> Convert(ACG.Polygon target)
  {
    // https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic30235.html
    int partCount = target.PartCount;
    if (partCount == 0)
    {
      throw new ValidationException("ArcGIS Polygon contains no parts");
    }

    // declare Region elements
    List<SOG.Region> regions = new();
    SOG.Polyline? boundary = null;
    List<SOG.Polyline> innerLoops = new();

    // iterate through polugon parts: can be inner or outer curves,
    // can be multiple outer curves too (if multipolygon).
    for (int i = 0; i < partCount; i++)
    {
      // get the part polyline
      ACG.ReadOnlySegmentCollection segmentCollection = target.Parts[i];
      SOG.Polyline polyline = _segmentConverter.Convert(segmentCollection);

      if (!target.IsExteriorRing(i))
      {
        innerLoops.Add(polyline);
      }
      else
      {
        // save previous region (if exists)
        if (boundary is not null)
        {
          regions.Add(CreateRegion(boundary, innerLoops));
        }
        // reset values to start a new region
        boundary = polyline;
        innerLoops = [];
      }
    }
    // after all loops, create and add the last region to the list
    if (boundary is not null)
    {
      regions.Add(CreateRegion(boundary, innerLoops));
    }

    return regions;
  }

  private SOG.Region CreateRegion(SOG.Polyline boundary, List<SOG.Polyline> innerLoops)
  {
    // create display mesh from region loops
    var allLoops = new List<SOG.Polyline>() { boundary };
    allLoops.AddRange(innerLoops);
    SOG.Mesh displayMesh = MeshFromLoops(allLoops);

    SOG.Region newRegion =
      new()
      {
        boundary = boundary,
        innerLoops = innerLoops.Cast<ICurve>().ToList(),
        hasHatchPattern = false,
        displayValue = [displayMesh],
        units = _settingsStore.Current.SpeckleUnits
      };
    return newRegion;
  }

  private SOG.Mesh MeshFromLoops(List<SOG.Polyline> loops)
  {
    // turn Polylines into Polyfaces (boundary will be the first in the list)
    var polyFaces = new List<Poly3>();
    foreach (var loop in loops)
    {
      var vertices = new List<Vector3>();
      for (int i = 0; i < loop.value.Count; i += 3)
      {
        vertices.Add(new Vector3(loop.value[i], loop.value[i + 1], loop.value[i + 2]));
      }
      polyFaces.Add(new Poly3(vertices));
    }

    var generator = new MeshGenerator(new BaseTransformer(), new LibTessTriangulator());
    var mesh3 = generator.TriangulateSurface(polyFaces);

    return Mesh3ToSpeckleMesh(mesh3);
  }

  private SOG.Mesh Mesh3ToSpeckleMesh(Mesh3 mesh3)
  {
    // copied from Tekla Solid converter, possibly to be moved to Speckle.Common
    var vertices = new List<double>();
    var faces = new List<int>();

    foreach (var v in mesh3.Vertices)
    {
      vertices.Add(v.X);
      vertices.Add(v.Y);
      vertices.Add(v.Z);
    }

    for (int i = 0; i < mesh3.Triangles.Count; i += 3)
    {
      faces.Add(3);
      faces.Add(mesh3.Triangles[i]);
      faces.Add(mesh3.Triangles[i + 1]);
      faces.Add(mesh3.Triangles[i + 2]);
    }

    var mesh = new SOG.Mesh
    {
      vertices = vertices,
      faces = faces,
      units = _settingsStore.Current.SpeckleUnits
    };

    return mesh;
  }
}
