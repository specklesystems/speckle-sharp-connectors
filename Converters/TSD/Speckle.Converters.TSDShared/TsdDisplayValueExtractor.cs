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
  private readonly TsdConversionSettings _settings;
  private readonly TsdVolumetricDisplayValueExtractor _volumetricExtractor;
  private readonly MeshGenerator _meshGenerator = new(new BaseTransformer(), new LibTessTriangulator());

  public TsdDisplayValueExtractor(
    ITsdModelDataProvider applicationService,
    TsdConversionSettings settings,
    TsdVolumetricDisplayValueExtractor volumetricExtractor
  )
  {
    _applicationService = applicationService;
    _settings = settings;
    _volumetricExtractor = volumetricExtractor;
  }

  /// <summary>
  /// The member's display value, plus its analytical span lines as centerlines when volumetric geometry is requested
  /// (empty otherwise) — one per span, in span order (ENG-9048).
  /// </summary>
  public async Task<(List<Base> Display, List<SOG.Line> Centerlines)> GetMemberDisplayValueAsync(
    IReadOnlyList<IMemberSpan> spans,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    if (spans.Count == 0)
    {
      return (new List<Base>(), new List<SOG.Line>());
    }

    var lines = await GetSpanLinesAsync(spans, unit, speckleUnits).ConfigureAwait(false);
    if (!_settings.SendVolumetricGeometry)
    {
      return (lines.Cast<Base>().ToList(), new List<SOG.Line>());
    }

    var solids = await _volumetricExtractor.TryExtrudeMemberAsync(spans, unit, speckleUnits).ConfigureAwait(false);
    return (solids ?? lines.Cast<Base>().ToList(), lines);
  }

  private async Task<List<SOG.Line>> GetSpanLinesAsync(
    IReadOnlyList<IMemberSpan> spans,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    var lines = new List<SOG.Line>();
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
      return lines;
    }

    var coordinates = await ConvertFromBaseAsync(baseCoordinates, unit).ConfigureAwait(false);

    for (int i = 0; i + 5 < coordinates.Count; i += 6)
    {
      lines.Add(
        new SOG.Line
        {
          start = new SOG.Point(coordinates[i], coordinates[i + 1], coordinates[i + 2], speckleUnits),
          end = new SOG.Point(coordinates[i + 3], coordinates[i + 4], coordinates[i + 5], speckleUnits),
          units = speckleUnits,
        }
      );
    }

    return lines;
  }

  public async Task<List<Base>> GetSlabDisplayValueAsync(ISlabItem slabItem, IUnitBase? unit, string speckleUnits)
  {
    if (_settings.SendVolumetricGeometry)
    {
      var solids = await _volumetricExtractor.TryExtrudeSlabAsync(slabItem, unit, speckleUnits).ConfigureAwait(false);
      if (solids is not null)
      {
        return solids;
      }
    }

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
      var outer = TsdRings.LiftRing(contour.Contour.Value, plane);
      if (outer.Count < 3)
      {
        continue;
      }

      var holes = contour
        .Holes.Select(hole => TsdRings.LiftRing(hole.Value, plane))
        .Where(hole => hole.Count >= 3)
        .ToList();

      if (holes.Count == 0)
      {
        AddNgonFace(baseVertices, faces, outer);
      }
      else
      {
        var polygons = new List<Poly3> { new(outer) };
        polygons.AddRange(holes.Select(hole => new Poly3(hole)));
        AddTriangles(baseVertices, faces, _meshGenerator.TriangulateSurface(polygons));
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
    if (_settings.SendVolumetricGeometry)
    {
      var solids = await _volumetricExtractor.TryExtrudeWallAsync(panels, unit, speckleUnits).ConfigureAwait(false);
      if (solids is not null)
      {
        return solids;
      }
    }

    var baseVertices = new List<double>();
    var faces = new List<int>();

    foreach (var panel in panels)
    {
      var quad = TsdRings.WallPanelQuad(panel);
      if (quad is null)
      {
        continue;
      }

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
}
