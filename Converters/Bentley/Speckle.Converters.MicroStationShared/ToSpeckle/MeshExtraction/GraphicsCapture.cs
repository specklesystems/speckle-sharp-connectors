using Speckle.Converters.MicroStation.Services;
using Speckle.Converters.MicroStation.ToSpeckle.Raw;
using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.MeshExtraction;

/// <summary>What one <see cref="DPN.ElementGraphicsOutput.Process"/> pass recovered.</summary>
public sealed class CapturedGraphics
{
  public List<Base> Meshes { get; } = [];
  public List<Base> Curves { get; } = [];
  public bool Any => Meshes.Count > 0 || Curves.Count > 0;
}

/// <summary>
/// The universal tessellation path — the managed counterpart of dgnextract's STL fallback, built on
/// <see cref="DPN.ElementGraphicsOutput"/> (the API Bentley documents for element → mesh work).
/// <see cref="DPN.ElementGraphicsProcessor.ProcessAsFacets(bool)"/> returns true so every surface,
/// SmartSolid/B-rep, cone and proxy is announced as a <see cref="BG.PolyfaceHeader"/>;
/// open linework arrives via ProcessCurveVector/ProcessCurvePrimitive and is captured only when the
/// caller asks (proxy graphics recovery), keeping the mesh fallback mesh-only like the STL path.
/// <para>
/// ⚠ CSE history: earlier attempts at graphics-engine calls tore down the host process
/// (see the May-2026 notes in this repo's git history). This processor does no drawing — it only
/// receives data callbacks — but every Process call is still guarded, and the dispatcher's
/// bounding-box fallback keeps Send alive if this path has to be disabled for an element type.
/// </para>
/// </summary>
public sealed class FacetCaptureProcessor : DPN.ElementGraphicsProcessor
{
  private readonly GeometryMapper _mapper;
  private readonly PolyfaceConverter _polyfaceConverter;
  private readonly CurvePrimitiveConverter _primitiveConverter;
  private readonly bool _wantCurves;
  private readonly CapturedGraphics _result;

  // Chord tolerance as a fraction of… nothing element-specific here: rely on the pipeline default
  // (FacetOptions.New) plus a triangles-only cap, mirroring MeshExtractor's deviation policy.
  private BG.FacetOptions? _facetOptions;
  private BG.DTransform3d? _currentTransform;

  public FacetCaptureProcessor(
    GeometryMapper mapper,
    PolyfaceConverter polyfaceConverter,
    CurvePrimitiveConverter primitiveConverter,
    bool wantCurves,
    CapturedGraphics result
  )
  {
    _mapper = mapper;
    _polyfaceConverter = polyfaceConverter;
    _primitiveConverter = primitiveConverter;
    _wantCurves = wantCurves;
    _result = result;
  }

  public override bool ProcessAsFacets(bool isPolyface) => true;

  public override bool ProcessAsBody(bool isCurved) => false;

  public override bool ExpandLineStyles() => false;

  public override bool ExpandPatterns() => false;

  public override bool WantClipping() => false;

  public override BG.FacetOptions GetFacetOptions()
  {
    if (_facetOptions == null)
    {
      _facetOptions = BG.FacetOptions.New();
      _facetOptions.NormalsRequired = false;
      _facetOptions.ParamsRequired = false;
      _facetOptions.EdgeHiding = false;
      _facetOptions.MaxPerFace = 3; // fan-triangulated output, consistent with the STL/terrain paths
    }
    return _facetOptions;
  }

  public override void AnnounceTransform(BG.DTransform3d trans) => _currentTransform = trans;

  public override void AnnounceIdentityTransform() => _currentTransform = null;

  public override DPN.BentleyStatus ProcessFacets(BG.PolyfaceHeader meshData, bool filled)
  {
    using IDisposable? scope = _currentTransform is BG.DTransform3d t ? _mapper.PushTransform(t) : null;
    SOG.Mesh? mesh = _polyfaceConverter.Convert(meshData);
    if (mesh != null)
    {
      _result.Meshes.Add(mesh);
    }
    return DPN.BentleyStatus.Success;
  }

  public override DPN.BentleyStatus ProcessCurveVector(BG.CurveVector curves, bool isFilled)
  {
    if (!_wantCurves)
    {
      return DPN.BentleyStatus.Success;
    }
    using IDisposable? scope = _currentTransform is BG.DTransform3d t ? _mapper.PushTransform(t) : null;
    new CurveVectorConverter(_mapper, _primitiveConverter).Convert(curves, _result.Curves);
    return DPN.BentleyStatus.Success;
  }

  public override DPN.BentleyStatus ProcessCurvePrimitive(BG.CurvePrimitive curve, bool isClosed, bool isFilled)
  {
    if (!_wantCurves)
    {
      return DPN.BentleyStatus.Success;
    }
    using IDisposable? scope = _currentTransform is BG.DTransform3d t ? _mapper.PushTransform(t) : null;
    _primitiveConverter.Convert(curve, _result.Curves);
    return DPN.BentleyStatus.Success;
  }
}

/// <summary>Runs a guarded facet-capture pass over one element.</summary>
public class GraphicsCaptureExtractor(
  GeometryMapper mapper,
  PolyfaceConverter polyfaceConverter,
  CurvePrimitiveConverter primitiveConverter
)
{
  /// <summary>
  /// Tessellates (and optionally recovers linework from) the element's graphics. Returns an empty
  /// capture when the pipeline yields nothing or throws — callers fall through to their next
  /// strategy (dgnextract's "returns false when the element yields no facets").
  /// </summary>
  public CapturedGraphics Capture(MgdElement element, bool wantCurves)
  {
    var result = new CapturedGraphics();
    try
    {
      var processor = new FacetCaptureProcessor(mapper, polyfaceConverter, primitiveConverter, wantCurves, result);
      DPN.ElementGraphicsOutput.Process(element, processor);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      // Swallow: graphics recovery is always a best-effort stage with a fallback behind it.
    }
    return result;
  }
}
