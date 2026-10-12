using Speckle.Common.StructuralExtrusion;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using TSD.API.Remoting.Common.Properties;
using TSD.API.Remoting.Geometry;
using TSD.API.Remoting.Sections;
using TSD.API.Remoting.Structure;
using TSD.API.Remoting.Units;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converters.TSDShared;

/// <summary>
/// Inflates TSD analytical geometry into solids: one section prism per member span, slab contours and wall panels
/// extruded half their thickness each way. Everything is built in TSD base units (mm) and converted once at the end.
/// Null means "keep the wireframe" and is recorded per element (ENG-9048).
/// </summary>
public sealed class TsdVolumetricDisplayValueExtractor
{
  private const string MEMBER = "Member";
  private const string SLAB = "Slab";
  private const string WALL = "Wall";

  // Same vertical-member rule as the CSi adapter: sine of the angle to global Z below this.
  private const double VERTICAL_TOLERANCE = 1e-3;

  private readonly ITsdModelDataProvider _dataProvider;
  private readonly TsdSectionPrismResolver _prismResolver;
  private readonly ExtrusionFallbackTracker _fallbacks;

  public TsdVolumetricDisplayValueExtractor(
    ITsdModelDataProvider dataProvider,
    TsdSectionPrismResolver prismResolver,
    ExtrusionFallbackTracker fallbacks
  )
  {
    _dataProvider = dataProvider;
    _prismResolver = prismResolver;
    _fallbacks = fallbacks;
  }

  public async Task<List<Base>?> TryExtrudeMemberAsync(
    IReadOnlyList<IMemberSpan> spans,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    try
    {
      if (spans.Count == 0)
      {
        return null;
      }

      var prisms = new List<PrismMesh>(spans.Count);
      foreach (var span in spans)
      {
        var prism = TryExtrudeSpan(span, out string? reason);
        if (prism is null)
        {
          return Fallback(MEMBER, reason ?? "unknown");
        }
        prisms.Add(prism);
      }

      return await ToMeshesAsync(prisms, unit, speckleUnits).ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return Fallback(MEMBER, ex.GetType().Name);
    }
  }

  public async Task<List<Base>?> TryExtrudeSlabAsync(ISlabItem slabItem, IUnitBase? unit, string speckleUnits)
  {
    try
    {
      var plane = slabItem.ElementPlane.Value;
      if (plane is null)
      {
        return Fallback(SLAB, "no-plane");
      }

      double depth = slabItem.SlabItemData.Value.Depth.Value;
      if (double.IsNaN(depth) || depth <= 0)
      {
        return Fallback(SLAB, "no-thickness");
      }

      var contours = await slabItem.GetContoursAsync(SlabContourType.Complete).ConfigureAwait(false);
      if (contours is null)
      {
        return Fallback(SLAB, "no-contour");
      }

      var prisms = new List<PrismMesh>();
      foreach (var contour in contours)
      {
        var outer = TsdRings.LiftRing(contour.Contour.Value, plane);
        if (outer.Count < 3)
        {
          continue;
        }

        var prism = PrismBuilder.TryExtrudeOutline(outer, depth);
        if (prism is null)
        {
          return Fallback(SLAB, "degenerate-outline");
        }
        prisms.Add(prism);
      }

      return prisms.Count == 0
        ? Fallback(SLAB, "no-contour")
        : await ToMeshesAsync(prisms, unit, speckleUnits).ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return Fallback(SLAB, ex.GetType().Name);
    }
  }

  public async Task<List<Base>?> TryExtrudeWallAsync(
    IReadOnlyList<IStructuralWallPanel> panels,
    IUnitBase? unit,
    string speckleUnits
  )
  {
    try
    {
      var prisms = new List<PrismMesh>(panels.Count);
      foreach (var panel in panels)
      {
        var quad = TsdRings.WallPanelQuad(panel);
        if (quad is null)
        {
          continue;
        }

        double thickness = panel.WallPanelData.Value.Thickness.Value;
        if (double.IsNaN(thickness) || thickness <= 0)
        {
          return Fallback(WALL, "no-thickness");
        }

        var prism = PrismBuilder.TryExtrudeOutline(quad, thickness);
        if (prism is null)
        {
          return Fallback(WALL, "degenerate-outline");
        }
        prisms.Add(prism);
      }

      return prisms.Count == 0 ? null : await ToMeshesAsync(prisms, unit, speckleUnits).ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return Fallback(WALL, ex.GetType().Name);
    }
  }

  private PrismMesh? TryExtrudeSpan(IMemberSpan span, out string? reason)
  {
    reason = null;
    var segment = span.DesignSegment.Value;
    if (segment is null)
    {
      reason = "no-segment";
      return null;
    }

    if (
      span.ElementSection.Value is not IMemberSection memberSection
      || memberSection.PhysicalSection.Value is not { } section
    )
    {
      reason = "no-section";
      return null;
    }

    var prism = _prismResolver.Resolve(section);
    if (prism.Template is null)
    {
      reason = prism.ShapeKey;
      return null;
    }

    var start = TsdRings.ToVector(segment.GetPoint(Location.Start));
    var end = TsdRings.ToVector(segment.GetPoint(Location.End));
    var direction = end - start;
    double length = direction.Length();
    if (double.IsNaN(length) || length <= 0)
    {
      reason = "zero-length";
      return null;
    }

    // Default axes as for CSi members: the section depth lies in the vertical plane through the member, or along
    // global +X for vertical members; the span rotation angle (radians) then turns depth towards width about the axis.
    double sineToVertical = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y) / length;
    var reference = sineToVertical < VERTICAL_TOLERANCE ? Vector3.UnitX : Vector3.UnitZ;
    var frame = LocalFrame.TryCreate(direction, reference, span.RotationAngle.Value);
    if (frame is null)
    {
      reason = "degenerate-axes";
      return null;
    }

    // TSD resolves alignment (snap level + offset) into per-end total offsets along the major (depth) and minor
    // (width) axes, applied here in the +major / +minor directions of the frame.
    var startOffset = new Vector2(ReadOrZero(span.StartMajorTotalOffset), ReadOrZero(span.StartMinorTotalOffset));
    var endOffset = new Vector2(ReadOrZero(span.EndMajorTotalOffset), ReadOrZero(span.EndMinorTotalOffset));
    return PrismBuilder.Place(prism.Template, start, frame.Value, length, startOffset, endOffset);
  }

  private static double ReadOrZero(IReadOnlyProperty<double> property)
  {
    try
    {
      double value = property.Value;
      return double.IsNaN(value) ? 0 : value;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return 0;
    }
  }

  private List<Base>? Fallback(string elementType, string reason)
  {
    _fallbacks.Record(elementType, reason);
    return null;
  }

  // One unit conversion round-trip for all prisms of an element, then split back into one mesh per prism.
  private async Task<List<Base>> ToMeshesAsync(IReadOnlyList<PrismMesh> prisms, IUnitBase? unit, string speckleUnits)
  {
    var baseVertices = new List<double>();
    foreach (var prism in prisms)
    {
      baseVertices.AddRange(prism.Vertices);
    }

    IReadOnlyList<double> vertices = unit is null
      ? baseVertices
      : await _dataProvider.ConvertFromBaseAsync(baseVertices, unit).ConfigureAwait(false);

    var meshes = new List<Base>(prisms.Count);
    int offset = 0;
    foreach (var prism in prisms)
    {
      int count = prism.Vertices.Count;
      var slice = new List<double>(count);
      for (int i = 0; i < count; i++)
      {
        slice.Add(vertices[offset + i]);
      }
      offset += count;

      meshes.Add(
        new SOG.Mesh
        {
          vertices = slice,
          faces = prism.Faces.ToList(),
          units = speckleUnits,
        }
      );
    }
    return meshes;
  }
}
