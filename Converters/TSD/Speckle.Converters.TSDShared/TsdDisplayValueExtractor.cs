using Speckle.Common.MeshTriangulation;
using Speckle.DoubleNumerics;
using Speckle.Sdk.Models;
using TSD.API.Remoting.Geometry;
using TSD.API.Remoting.Structure;
using TSD.API.Remoting.Units;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converters.TSDShared;

public sealed class TsdDisplayValueExtractor
{
  private readonly ITsdModelDataProvider _applicationService;

  public TsdDisplayValueExtractor(ITsdModelDataProvider applicationService)
  {
    _applicationService = applicationService;
  }

  public async Task<List<Base>> GetMemberDisplayValueAsync(
    IReadOnlyList<IMemberSpan> spans,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    var displayValue = new List<Base>();

    if (spans.Count == 0)
    {
      return displayValue;
    }

    var baseCoordinates = new List<double>();
    foreach (var span in spans)
    {
      var segment = span.DesignSegment.Value;
      if (segment is null)
      {
        continue;
      }

      var start = segment.GetPoint(Location.Start);
      var end = segment.GetPoint(Location.End);

      baseCoordinates.Add(start.X);
      baseCoordinates.Add(start.Y);
      baseCoordinates.Add(start.Z);
      baseCoordinates.Add(end.X);
      baseCoordinates.Add(end.Y);
      baseCoordinates.Add(end.Z);
    }

    if (baseCoordinates.Count == 0)
    {
      return displayValue;
    }

    var coordinates = await ConvertFromBaseAsync(baseCoordinates, unit).ConfigureAwait(false);

    for (int i = 0; i + 5 < coordinates.Count; i += 6)
    {
      displayValue.Add(
        new SOG.Line
        {
          start = new SOG.Point(coordinates[i], coordinates[i + 1], coordinates[i + 2], speckleUnits),
          end = new SOG.Point(coordinates[i + 3], coordinates[i + 4], coordinates[i + 5], speckleUnits),
          units = speckleUnits,
        }
      );
    }

    return displayValue;
  }

  public async Task<List<Base>> GetSlabDisplayValueAsync(ISlabItem slabItem, IUnitBase? unit, string speckleUnits)
  {
    var plane = slabItem.ElementPlane.Value;
    if (plane is null)
    {
      return new List<Base>();
    }

    var contours = await slabItem.GetContoursAsync(SlabContourType.Complete).ConfigureAwait(false);
    if (contours is null)
    {
      return new List<Base>();
    }

    var baseVertices = new List<double>();
    var faces = new List<int>();

    foreach (var contour in contours)
    {
      var outer = LiftRing(contour.Contour.Value, plane);
      if (outer.Count < 3)
      {
        continue;
      }

      var holes = contour.Holes.Select(hole => LiftRing(hole.Value, plane)).Where(hole => hole.Count >= 3).ToList();

      if (holes.Count == 0)
      {
        AddNgonFace(baseVertices, faces, outer);
      }
      else
      {
        var polygons = new List<Poly3> { new(outer) };
        polygons.AddRange(holes.Select(hole => new Poly3(hole)));
        AddTriangles(
          baseVertices,
          faces,
          new MeshGenerator(new BaseTransformer(), new LibTessTriangulator()).TriangulateSurface(polygons)
        );
      }
    }

    return await BuildMeshAsync(baseVertices, faces, unit, speckleUnits).ConfigureAwait(false);
  }

  public async Task<List<Base>> GetWallDisplayValueAsync(
    IReadOnlyList<IStructuralWallPanel> panels,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    var baseVertices = new List<double>();
    var faces = new List<int>();

    foreach (var panel in panels)
    {
      var bottom = panel.BottomSegment.Value;
      var top = panel.TopSegment.Value;
      if (bottom is null || top is null)
      {
        continue;
      }

      var quad = new List<Vector3>
      {
        ToVector(bottom.GetPoint(Location.Start)),
        ToVector(bottom.GetPoint(Location.End)),
        ToVector(top.GetPoint(Location.End)),
        ToVector(top.GetPoint(Location.Start)),
      };

      AddNgonFace(baseVertices, faces, quad);
    }

    return await BuildMeshAsync(baseVertices, faces, unit, speckleUnits).ConfigureAwait(false);
  }

  private async Task<List<Base>> BuildMeshAsync(
    List<double> baseVertices,
    List<int> faces,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    if (baseVertices.Count == 0)
    {
      return new List<Base>();
    }

    var vertices = await ConvertFromBaseAsync(baseVertices, unit).ConfigureAwait(false);

    return new List<Base>
    {
      new SOG.Mesh
      {
        vertices = vertices.ToList(),
        faces = faces,
        units = speckleUnits,
      },
    };
  }

  private async Task<IReadOnlyList<double>> ConvertFromBaseAsync(List<double> baseValues, IUnitBase? unit) =>
    unit is null ? baseValues : await _applicationService.ConvertFromBaseAsync(baseValues, unit).ConfigureAwait(false);

  private static List<Vector3> LiftRing(IPolygon2D polygon, IPlane plane)
  {
    var ring = new List<Vector3>();
    foreach (var vertex in polygon.Vertices)
    {
      var point = plane.Local2Global(vertex.Value);
      ring.Add(new Vector3(point.X, point.Y, point.Z));
    }

    if (ring.Count > 1 && (ring[^1] - ring[0]).Length() < 1e-6)
    {
      ring.RemoveAt(ring.Count - 1);
    }

    return ring;
  }

  private static void AddNgonFace(List<double> vertices, List<int> faces, List<Vector3> ring)
  {
    int start = vertices.Count / 3;
    faces.Add(ring.Count);
    for (int i = 0; i < ring.Count; i++)
    {
      vertices.Add(ring[i].X);
      vertices.Add(ring[i].Y);
      vertices.Add(ring[i].Z);
      faces.Add(start + i);
    }
  }

  private static void AddTriangles(List<double> vertices, List<int> faces, Mesh3 mesh)
  {
    int start = vertices.Count / 3;
    foreach (var vertex in mesh.Vertices)
    {
      vertices.Add(vertex.X);
      vertices.Add(vertex.Y);
      vertices.Add(vertex.Z);
    }

    for (int i = 0; i + 2 < mesh.Triangles.Count; i += 3)
    {
      faces.Add(3);
      faces.Add(start + mesh.Triangles[i]);
      faces.Add(start + mesh.Triangles[i + 1]);
      faces.Add(start + mesh.Triangles[i + 2]);
    }
  }

  private static Vector3 ToVector(Point3D point) => new(point.X, point.Y, point.Z);
}
