using Microsoft.Extensions.Logging;
using Speckle.Converters.MicroStation.Services;
using Speckle.Converters.MicroStation.ToSpeckle.Appearance;
using Speckle.Converters.MicroStation.ToSpeckle.MeshExtraction;
using Speckle.Converters.MicroStation.ToSpeckle.Raw;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle;

/// <summary>One converted geometry piece plus its appearance channel (material XOR colour).</summary>
public sealed class ExtractedGeometry
{
  public required Base Geometry { get; init; }

  /// <summary>Display colour (ARGB) — set when no material applies, and always for curves.</summary>
  public int? ColorArgb { get; init; }

  /// <summary>Real MicroStation material — meshes only, mutually exclusive with ColorArgb.</summary>
  public ResolvedMaterial? Material { get; init; }
}

/// <summary>
/// The recursive display-value dispatcher — the managed port of dgnextract's <c>Dispatch::extract</c>,
/// same strategy order:
/// <list type="number">
/// <item>mesh elements: read face loops off the element (<see cref="MgdElements.MeshHeaderElement.GetMeshData"/>)</item>
/// <item>shared cells: BAKED here (definition children through the placement transform) — top-level
/// shared cells never reach this dispatcher, the connector's instance unpacker turns them into
/// InstanceProxies and drives definition extraction itself</item>
/// <item>normal cells: B-rep gate — <see cref="MgdElements.BrepCellHeaderElement"/> (SmartSolid)
/// tessellates whole, never decomposes into wireframe; other cells recurse children with the
/// header's colour pushed as the ByCell context</item>
/// <item>proxies / extended elements (OpenRoads leftovers, terrain, Bentley-vertical elements):
/// facet capture first, curve recovery second, child recursion last</item>
/// <item>text → typed Speckle Text</item>
/// <item>typed curve suite via <see cref="MgdElements.CurvePathQuery.ElementToCurveVector"/>;
/// filled closed regions additionally contribute their fill mesh from the graphics capture</item>
/// <item>facet-capture fallback (the STL-fallback equivalent), then the bounding-box mesh of last
/// resort — a displayable element never converts to nothing</item>
/// </list>
/// Appearance rule (ENG-9130): meshes take a real material when one resolves, otherwise the
/// symbology colour; curves and points always take the colour and never a material.
/// </summary>
public class DisplayValueExtractor(
  GeometryMapper mapper,
  GraphicsCaptureExtractor graphicsCapture,
  PolyfaceConverter polyfaceConverter,
  CurvePrimitiveConverter curvePrimitiveConverter,
  AppearanceResolver appearance,
  TextConverter textConverter,
  ILogger<DisplayValueExtractor> logger
)
{
  private const int MAX_DEPTH = 16;
  private int _depth;

  // Bake-path cycle guard: shared-cell definition ids currently being baked through.
  private readonly HashSet<string> _activeBakes = [];

  public List<ExtractedGeometry> Extract(MgdElement element)
  {
    var output = new List<ExtractedGeometry>();
    ExtractInto(element, output);
    return output;
  }

  private void ExtractInto(MgdElement element, List<ExtractedGeometry> output)
  {
    if (_depth > MAX_DEPTH || !element.IsGraphics)
    {
      return;
    }
    _depth++;
    try
    {
      ExtractCore(element, output);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      logger.LogWarning(ex, "Element {Id} extraction failed at stage dispatch; using fallback.", ElementIdOf(element));
      AddBoundingBoxFallback(element, output);
    }
    finally
    {
      _depth--;
    }
  }

  private void ExtractCore(MgdElement element, List<ExtractedGeometry> output)
  {
    if (element.IsInvisible || IsConstructionClass(element))
    {
      return;
    }

    // 1. Mesh elements — native face-loop read is the primary path (ENG-8775: graphics/STL export
    //    yields zero facets for OpenRoads corridor component meshes).
    if (element is MgdElements.MeshHeaderElement meshHeader)
    {
      SOG.Mesh? mesh = ConvertMeshData(meshHeader);
      if (mesh != null)
      {
        output.Add(WithMeshAppearance(element, mesh));
        return;
      }
      CaptureMeshes(element, output, required: true);
      return;
    }

    // 2. Shared cells inside a recursion (cells/definitions) — bake the definition geometry
    //    through the instance placement. Top-level instancing lives in the connector unpacker.
    if (element is MgdElements.SharedCellElement sharedCell)
    {
      BakeSharedCell(sharedCell, output);
      return;
    }

    // 3. Normal cells: B-rep tessellates whole; everything else recurses with ByCell colour context.
    if (element is MgdElements.BrepCellHeaderElement)
    {
      CaptureMeshes(element, output, required: true);
      return;
    }
    if (element is MgdElements.CellHeaderElement or MgdElements.Type2Element)
    {
      RecurseChildren(element, output);
      return;
    }

    // 4. Text — typed Speckle Text objects.
    if (element is MgdElements.TextElement or MgdElements.TextNodeElement)
    {
      int color = appearance.ResolveColorArgb(element);
      bool anyText = false;
      if (MgdElements.TextQuery.GetAsTextQuery(element) is { } textQuery)
      {
        foreach (var text in textConverter.Convert(textQuery))
        {
          output.Add(new ExtractedGeometry { Geometry = text, ColorArgb = color });
          anyText = true;
        }
      }
      if (!anyText)
      {
        CaptureAnything(element, output);
      }
      return;
    }

    // 5. Extended elements (OpenRoads proxies, terrain, vertical-app elements): facet capture
    //    first, curve recovery second, child recursion last.
    if (element is MgdElements.ExtendedElementElement)
    {
      CapturedGraphics captured = graphicsCapture.Capture(element, wantCurves: true);
      if (captured.Meshes.Count > 0)
      {
        AddMeshes(element, captured.Meshes, output);
        return;
      }
      if (captured.Curves.Count > 0)
      {
        AddCurves(element, captured.Curves, output);
        return;
      }
      int before = output.Count; // output is the shared accumulator — compare against THIS element's yield
      RecurseChildren(element, output);
      if (output.Count == before)
      {
        AddBoundingBoxFallback(element, output);
      }
      return;
    }

    // 6. Typed curve suite. CurvePathQuery covers line/linestring/shape/arc/ellipse/bspline/
    //    complex chains/point strings/curves/multilines in one call, primitives kept typed.
    BG.CurveVector? curveVector = TryGetCurveVector(element);
    if (curveVector != null)
    {
      var curves = new List<Base>();
      new CurveVectorConverter(mapper, curvePrimitiveConverter).Convert(curveVector, curves);
      if (curves.Count > 0)
      {
        AddCurves(element, curves, output);

        // Fill mesh for closed regions (FilledElementPolicy): the graphics pipeline emits fill
        // facets exactly when the region carries fill, so capture is the policy.
        BG.CurveVector.BoundaryType boundary = curveVector.GetBoundaryType();
        if (boundary is not BG.CurveVector.BoundaryType.Open and not BG.CurveVector.BoundaryType.None)
        {
          CapturedGraphics fill = graphicsCapture.Capture(element, wantCurves: false);
          AddMeshes(element, fill.Meshes, output);
        }
        return;
      }
    }

    // 7. Everything else (solids, surfaces, cones, bspline surfaces, dimensions, unknowns):
    //    facet capture, curve recovery, bounding box of last resort.
    CaptureAnything(element, output);
  }

  private void CaptureAnything(MgdElement element, List<ExtractedGeometry> output)
  {
    CapturedGraphics captured = graphicsCapture.Capture(element, wantCurves: true);
    if (captured.Meshes.Count > 0)
    {
      AddMeshes(element, captured.Meshes, output);
      return;
    }
    if (captured.Curves.Count > 0)
    {
      AddCurves(element, captured.Curves, output);
      return;
    }
    AddBoundingBoxFallback(element, output);
  }

  private void CaptureMeshes(MgdElement element, List<ExtractedGeometry> output, bool required)
  {
    CapturedGraphics captured = graphicsCapture.Capture(element, wantCurves: false);
    if (captured.Meshes.Count > 0)
    {
      AddMeshes(element, captured.Meshes, output);
      return;
    }
    if (required)
    {
      AddBoundingBoxFallback(element, output);
    }
  }

  private void BakeSharedCell(MgdElements.SharedCellElement sharedCell, List<ExtractedGeometry> output)
  {
    MgdElement? definition = SharedCellPlacement.FindDefinition(sharedCell);
    if (definition == null)
    {
      logger.LogWarning("Shared cell '{Name}' has no definition; skipped.", sharedCell.CellName);
      return;
    }
    string defId = ElementIdOf(definition);
    if (!_activeBakes.Add(defId))
    {
      logger.LogWarning("Cyclic shared cell definition {DefId}; branch stopped.", defId);
      return;
    }
    try
    {
      if (!SharedCellPlacement.TryCompute(sharedCell, definition, out BG.DTransform3d placement))
      {
        logger.LogWarning("Shared cell '{Name}' placement unavailable; skipped.", sharedCell.CellName);
        return;
      }
      using IDisposable scope = mapper.PushTransform(placement);
      MgdElements.ChildElementCollection? children = definition.GetChildren();
      if (children != null)
      {
        foreach (MgdElement? child in children)
        {
          if (child != null)
          {
            ExtractInto(child, output);
          }
        }
      }
    }
    finally
    {
      _activeBakes.Remove(defId);
    }
  }

  private void RecurseChildren(MgdElement cell, List<ExtractedGeometry> output)
  {
    // ByCell colour context: children with ByCell colour resolve against the header.
    using IDisposable colorScope = appearance.PushParentColor(appearance.ResolveColorArgb(cell));
    bool any = false;
    MgdElements.ChildElementCollection? children = cell.GetChildren();
    if (children != null)
    {
      foreach (MgdElement? child in children)
      {
        if (child != null)
        {
          int before = output.Count;
          ExtractInto(child, output);
          any |= output.Count > before;
        }
      }
    }
    if (!any)
    {
      // A cell whose children all failed/skipped still tessellates as a whole (never vanishes).
      CaptureMeshes(cell, output, required: false);
    }
  }

  private SOG.Mesh? ConvertMeshData(MgdElements.MeshHeaderElement meshHeader)
  {
    try
    {
      BG.PolyfaceHeader? polyface = meshHeader.GetMeshData();
      return polyface != null ? polyfaceConverter.Convert(polyface) : null;
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      logger.LogWarning(ex, "MeshHeaderElement {Id} face-loop read failed.", ElementIdOf(meshHeader));
      return null;
    }
  }

  private BG.CurveVector? TryGetCurveVector(MgdElement element)
  {
    // Only ask the curve suite about actual curve-bearing element kinds; solids/surfaces would
    // otherwise round-trip their edges as wireframe (dgnextract never decomposes surfaces).
    if (
      element
      is not (
        MgdElements.LineElement
        or MgdElements.LineStringBaseElement
        or MgdElements.EllipticArcBaseElement
        or MgdElements.ChainHeaderElement
        or MgdElements.CurveElement
        or MgdElements.PointStringElement
        or MgdElements.MultilineElement
        or MgdElements.BSplineCurveElement
      )
    )
    {
      return null;
    }
    // ChainHeaderElement covers ComplexShape/ComplexString/BSplineCurve; exclude surfaces that
    // also derive from ComplexHeaderElement by not listing them above.
    try
    {
      return MgdElements.CurvePathQuery.ElementToCurveVector(element);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      return null;
    }
  }

  private void AddMeshes(MgdElement element, List<Base> meshes, List<ExtractedGeometry> output)
  {
    if (meshes.Count == 0)
    {
      return;
    }
    ResolvedMaterial? material = appearance.ResolveMaterial(element);
    int? color = material == null ? appearance.ResolveColorArgb(element) : null;
    foreach (Base mesh in meshes)
    {
      output.Add(
        new ExtractedGeometry
        {
          Geometry = mesh,
          Material = material,
          ColorArgb = color,
        }
      );
    }
  }

  private void AddCurves(MgdElement element, List<Base> curves, List<ExtractedGeometry> output)
  {
    // Curves NEVER reference render materials — colour channel only (CurveExtractor.WithColors).
    int color = appearance.ResolveColorArgb(element);
    foreach (Base curve in curves)
    {
      output.Add(new ExtractedGeometry { Geometry = curve, ColorArgb = color });
    }
  }

  private void AddBoundingBoxFallback(MgdElement element, List<ExtractedGeometry> output)
  {
    if (element is not MgdElements.DisplayableElement displayable)
    {
      return;
    }
    try
    {
      if (!displayable.CalcElementRange(out BG.DRange3d range).Equals(DPN.StatusInt.Success))
      {
        return;
      }
      SOG.Mesh mesh = BoundingBoxMesh(range);
      if (mesh.vertices.Count > 0)
      {
        output.Add(WithMeshAppearance(element, mesh));
      }
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      logger.LogWarning(ex, "Bounding-box fallback failed for element {Id}.", ElementIdOf(element));
    }
  }

  private ExtractedGeometry WithMeshAppearance(MgdElement element, Base mesh)
  {
    ResolvedMaterial? material = appearance.ResolveMaterial(element);
    return new ExtractedGeometry
    {
      Geometry = mesh,
      Material = material,
      ColorArgb = material == null ? appearance.ResolveColorArgb(element) : null,
    };
  }

  private SOG.Mesh BoundingBoxMesh(BG.DRange3d range)
  {
    BG.DPoint3d lo = range.Low;
    BG.DPoint3d hi = range.High;
    BG.DPoint3d[] corners =
    [
      new()
      {
        X = lo.X,
        Y = lo.Y,
        Z = lo.Z,
      },
      new()
      {
        X = hi.X,
        Y = lo.Y,
        Z = lo.Z,
      },
      new()
      {
        X = hi.X,
        Y = hi.Y,
        Z = lo.Z,
      },
      new()
      {
        X = lo.X,
        Y = hi.Y,
        Z = lo.Z,
      },
      new()
      {
        X = lo.X,
        Y = lo.Y,
        Z = hi.Z,
      },
      new()
      {
        X = hi.X,
        Y = lo.Y,
        Z = hi.Z,
      },
      new()
      {
        X = hi.X,
        Y = hi.Y,
        Z = hi.Z,
      },
      new()
      {
        X = lo.X,
        Y = hi.Y,
        Z = hi.Z,
      },
    ];
    var vertices = new List<double>(24);
    foreach (BG.DPoint3d corner in corners)
    {
      var (x, y, z) = mapper.MapXyz(corner);
      vertices.Add(x);
      vertices.Add(y);
      vertices.Add(z);
    }
    List<int> faces =
    [
      .. new[] { 4, 0, 1, 2, 3, 4, 4, 5, 6, 7, 4, 0, 1, 5, 4, 4, 2, 3, 7, 6, 4, 1, 2, 6, 5, 4, 0, 3, 7, 4 },
    ];
    return new SOG.Mesh
    {
      vertices = vertices,
      faces = faces,
      units = mapper.Units,
    };
  }

  private static bool IsConstructionClass(MgdElement element)
  {
    if (element is not MgdElements.DisplayableElement displayable)
    {
      return false;
    }
    try
    {
      using DPN.ElementDisplayParameters displayParams = displayable.GetElementDisplayParameters(false);
      return displayParams.ElementClass is DPN.DgnElementClass.Construction or DPN.DgnElementClass.ConstructionRule;
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      return false;
    }
  }

  private static string ElementIdOf(MgdElement element) => ((ulong)element.ElementId).ToString();
}
