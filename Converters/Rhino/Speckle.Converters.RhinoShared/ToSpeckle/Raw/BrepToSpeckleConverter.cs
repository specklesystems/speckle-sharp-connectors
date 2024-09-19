using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class BrepToSpeckleConverter : ITypedConverter<RG.Brep, SOG.Brep>
{
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.Curve, ICurve> _curveConverter;
  private readonly ITypedConverter<RG.NurbsSurface, SOG.Surface> _surfaceConverter;
  private readonly ITypedConverter<RG.Mesh, SOG.Mesh> _meshConverter;
  private readonly ITypedConverter<RG.Box, SOG.Box> _boxConverter;
  private readonly ITypedConverter<RG.Interval, SOP.Interval> _intervalConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public BrepToSpeckleConverter(
    ITypedConverter<RG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<RG.Curve, ICurve> curveConverter,
    ITypedConverter<RG.NurbsSurface, SOG.Surface> surfaceConverter,
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter,
    ITypedConverter<RG.Box, SOG.Box> boxConverter,
    ITypedConverter<RG.Interval, SOP.Interval> intervalConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _curveConverter = curveConverter;
    _surfaceConverter = surfaceConverter;
    _meshConverter = meshConverter;
    _boxConverter = boxConverter;
    _intervalConverter = intervalConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Brep object to a Speckle Brep object.
  /// </summary>
  /// <param name="target">The Brep object to convert.</param>
  /// <returns>The converted Speckle Brep object.</returns>
  public SOG.Brep Convert(RG.Brep target)
  {
    var tol = _settingsStore.Current.Document.ModelAbsoluteTolerance;
    target.Repair(tol);

    // POC: CNX-9276 This should come as part of the user settings in the context object.
    // if (PreprocessGeometry)
    // {
    //   brep = BrepEncoder.ToRawBrep(brep, 1.0, Doc.ModelAngleToleranceRadians, Doc.ModelRelativeTolerance);
    // }

    // get display mesh and attach render material to it if it exists
    var displayMesh = GetBrepDisplayMesh(target);
    var displayValue = new List<SOG.Mesh>();
    if (displayMesh != null)
    {
      displayValue.Add(_meshConverter.Convert(displayMesh));
    }

    // POC: CNX-9277 Swap input material for something coming from the context.
    // if (displayValue != null && mat != null)
    // {
    //   displayValue["renderMaterial"] = mat;
    // }

    // Vertices, uv curves, 3d curves and surfaces
    List<SOG.Point> vertices = new(target.Vertices.Count);
    vertices.AddRange(target.Vertices.Select(v => _pointConverter.Convert(v.Location)));

    List<ICurve> curves3d = new(target.Curves3D.Count);
    curves3d.AddRange(target.Curves3D.Select(curve3d => _curveConverter.Convert(curve3d)));

    List<SOG.Surface> surfaces = new(target.Curves3D.Count);
    surfaces.AddRange(target.Surfaces.Select(srf => _surfaceConverter.Convert(srf.ToNurbsSurface())));

    List<ICurve> curves2d = new(target.Curves2D.Count);
    using (_settingsStore.Push(x => x with { SpeckleUnits = Units.None }))
    {
      // Curves2D are unitless, so we convert them within a new pushed context with None units.
      curves2d.AddRange(target.Curves2D.Select(curve2d => _curveConverter.Convert(curve2d)));
    }

    var speckleBrep = new SOG.Brep
    {
      Vertices = vertices,
      Curve3D = curves3d,
      Curve2D = curves2d,
      Surfaces = surfaces,
      displayValue = displayValue,
      IsClosed = target.IsSolid,
      Orientation = (SOG.BrepOrientation)target.SolidOrientation,
      volume = target.IsSolid ? target.GetVolume() : 0,
      area = target.GetArea(),
      bbox = _boxConverter.Convert(new RG.Box(target.GetBoundingBox(false))),
      units = _settingsStore.Current.SpeckleUnits,
      Edges = new(target.Edges.Count),
      Loops = new(target.Loops.Count),
      Trims = new(target.Trims.Count),
      Faces = new(target.Faces.Count)
    };

    // Brep non-geometry types
    ConvertBrepFaces(target, speckleBrep);
    ConvertBrepEdges(target, speckleBrep);
    ConvertBrepLoops(target, speckleBrep);
    ConvertBrepTrims(target, speckleBrep);

    return speckleBrep;
  }

  private static void ConvertBrepFaces(RG.Brep brep, SOG.Brep speckleParent)
  {
    foreach (var f in brep.Faces)
    {
      speckleParent.Faces.Add(
        new SOG.BrepFace
        {
          Brep = speckleParent,
          SurfaceIndex = f.SurfaceIndex,
          LoopIndices = f.Loops.Select(l => l.LoopIndex).ToList(),
          OuterLoopIndex = f.OuterLoop.LoopIndex,
          OrientationReversed = f.OrientationIsReversed,
        }
      );
    }
  }

  private void ConvertBrepEdges(RG.Brep brep, SOG.Brep speckleParent)
  {
    foreach (var edge in brep.Edges)
    {
      speckleParent.Edges.Add(
        new SOG.BrepEdge
        {
          Brep = speckleParent,
          Curve3dIndex = edge.EdgeCurveIndex,
          TrimIndices = edge.TrimIndices(),
          StartIndex = edge.StartVertex.VertexIndex,
          EndIndex = edge.EndVertex.VertexIndex,
          ProxyCurveIsReversed = edge.ProxyCurveIsReversed,
          Domain = _intervalConverter.Convert(edge.Domain),
        }
      );
    }
  }

  private void ConvertBrepTrims(RG.Brep brep, SOG.Brep speckleParent)
  {
    foreach (var trim in brep.Trims)
    {
      speckleParent.Trims.Add(
        new SOG.BrepTrim
        {
          Brep = speckleParent,
          EdgeIndex = trim.Edge?.EdgeIndex ?? -1,
          FaceIndex = trim.Face.FaceIndex,
          LoopIndex = trim.Loop.LoopIndex,
          CurveIndex = trim.TrimCurveIndex,
          IsoStatus = (int)trim.IsoStatus,
          TrimType = (SOG.BrepTrimType)trim.TrimType,
          IsReversed = trim.IsReversed(),
          StartIndex = trim.StartVertex.VertexIndex,
          EndIndex = trim.EndVertex.VertexIndex,
          Domain = _intervalConverter.Convert(trim.Domain),
        }
      );
    }
  }

  private void ConvertBrepLoops(RG.Brep brep, SOG.Brep speckleParent)
  {
    foreach (var loop in brep.Loops)
    {
      speckleParent.Loops.Add(
        new SOG.BrepLoop
        {
          Brep = speckleParent,
          FaceIndex = loop.Face.FaceIndex,
          TrimIndices = loop.Trims.Select(t => t.TrimIndex).ToList(),
          Type = (SOG.BrepLoopType)loop.LoopType,
        }
      );
    }
  }

  private RG.Mesh? GetBrepDisplayMesh(RG.Brep brep)
  {
    var joinedMesh = new RG.Mesh();

    // get from settings
    //Settings.TryGetValue("sendMeshSetting", out string meshSetting);

    RG.MeshingParameters mySettings = new(0.05, 0.05);
    // switch (SelectedMeshSettings)
    // {
    //   case MeshSettings.CurrentDoc:
    //     mySettings = RH.MeshingParameters.DocumentCurrentSetting(Doc);
    //     break;
    //   case MeshSettings.Default:
    //   default:
    //     mySettings = new RH.MeshingParameters(0.05, 0.05);
    //     break;
    // }

    try
    {
      joinedMesh.Append(RG.Mesh.CreateFromBrep(brep, mySettings));
      return joinedMesh;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return null;
    }
  }
}
