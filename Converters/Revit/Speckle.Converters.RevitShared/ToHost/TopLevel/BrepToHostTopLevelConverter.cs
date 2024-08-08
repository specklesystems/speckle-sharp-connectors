using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.ToSpeckle;
using Speckle.Objects;
using Speckle.Objects.Other;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Brep), 0)]
public sealed class BrepTopLevelConverterToHost
  : BaseTopLevelConverterToHost<SOG.Brep, DB.Solid>,
    ITypedConverter<SOG.Brep, DB.Solid>
{
  private readonly IRevitConversionContextStack _contextStack;
  private readonly ScalingServiceToHost _scalingService;
  private readonly ITypedConverter<RenderMaterial, DB.Material> _materialConverter;
  private readonly ITypedConverter<SOG.Surface, DB.BRepBuilderSurfaceGeometry> _surfaceConverter;
  private readonly ITypedConverter<ICurve, DB.CurveArray> _curveConverter;

  public BrepTopLevelConverterToHost(
    IRevitConversionContextStack contextStack,
    ScalingServiceToHost scalingService,
    ITypedConverter<RenderMaterial, DB.Material> materialConverter,
    ITypedConverter<SOG.Surface, DB.BRepBuilderSurfaceGeometry> surfaceConverter,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter
  )
  {
    _contextStack = contextStack;
    _scalingService = scalingService;
    _materialConverter = materialConverter;
    _surfaceConverter = surfaceConverter;
    _curveConverter = curveConverter;
  }

  public override DB.Solid Convert(SOG.Brep target)
  {
    //Make sure face references are calculated by revit
    var bRepType = DB.BRepType.OpenShell;
    switch (target.Orientation)
    {
      case SOG.BrepOrientation.Inward:
        bRepType = DB.BRepType.Void;
        break;
      case SOG.BrepOrientation.Outward:
        bRepType = DB.BRepType.Solid;
        break;
    }

    DB.ElementId? materialId = null;
    if (target["renderMaterial"] is RenderMaterial renderMaterial)
    {
      materialId = _materialConverter.Convert(renderMaterial).Id;
    }

    using var builder = new DB.BRepBuilder(bRepType);

    builder.SetAllowShortEdges();
    builder.AllowRemovalOfProblematicFaces();
    var brepEdges = new List<DB.BRepBuilderGeometryId>[target.Edges.Count];
    foreach (var face in target.Faces)
    {
      var faceId = builder.AddFace(_surfaceConverter.Convert(face.Surface), face.OrientationReversed);
      if (materialId is not null)
      {
        builder.SetFaceMaterialId(faceId, materialId);
      }

      foreach (var loop in face.Loops)
      {
        var loopId = builder.AddLoop(faceId);
        if (face.OrientationReversed)
        {
          loop.TrimIndices.Reverse();
        }

        foreach (var trim in loop.Trims)
        {
          if (
            trim.TrimType != SOG.BrepTrimType.Boundary
            && trim.TrimType != SOG.BrepTrimType.Mated
            && trim.TrimType != SOG.BrepTrimType.Seam
          )
          {
            continue;
          }

          if (trim.Edge == null)
          {
            continue;
          }

          var edgeIds = brepEdges[trim.EdgeIndex];
          if (edgeIds == null)
          {
            // First time we see this edge, convert it and add
            edgeIds = brepEdges[trim.EdgeIndex] = new List<DB.BRepBuilderGeometryId>();
            var bRepBuilderGeometryIds = BrepEdgeToNative(trim.Edge).Select(edge => builder.AddEdge(edge));
            edgeIds.AddRange(bRepBuilderGeometryIds);
          }

          var trimReversed = face.OrientationReversed ? !trim.IsReversed : trim.IsReversed;
          if (trimReversed)
          {
            for (int e = edgeIds.Count - 1; e >= 0; --e)
            {
              if (builder.IsValidEdgeId(edgeIds[e]))
              {
                builder.AddCoEdge(loopId, edgeIds[e], true);
              }
            }
          }
          else
          {
            for (int e = 0; e < edgeIds.Count; ++e)
            {
              if (builder.IsValidEdgeId(edgeIds[e]))
              {
                builder.AddCoEdge(loopId, edgeIds[e], false);
              }
            }
          }
        }
        builder.FinishLoop(loopId);
      }
      builder.FinishFace(faceId);
    }

    var bRepBuilderOutcome = builder.Finish();
    if (bRepBuilderOutcome == DB.BRepBuilderOutcome.Failure || !builder.IsResultAvailable())
    {
      throw new SpeckleConversionException("BRepBuilder failed for unknown reason");
    }

    var result = builder.GetResult();
    return result;
  }

  public List<DB.BRepBuilderEdgeGeometry> BrepEdgeToNative(SOG.BrepEdge edge)
  {
    // TODO: Trim curve with domain. Unsure if this is necessary as all our curves are converted to NURBS on Rhino output.
    var nativeCurveArray = _curveConverter.Convert(edge.Curve);
    bool isTrimmed =
      edge.Curve.domain != null
      && edge.Domain != null
      && (edge.Curve.domain.start != edge.Domain.start || edge.Curve.domain.end != edge.Domain.end);
    if (nativeCurveArray.Size == 1)
    {
      var nativeCurve = nativeCurveArray.get_Item(0);

      if (edge.ProxyCurveIsReversed)
      {
        nativeCurve = nativeCurve.CreateReversed();
      }

      if (nativeCurve == null)
      {
        return new List<DB.BRepBuilderEdgeGeometry>();
      }

      if (isTrimmed)
      {
        nativeCurve.MakeBound(edge.Domain?.start ?? 0, edge.Domain?.end ?? 1);
      }

      if (!nativeCurve.IsBound)
      {
        nativeCurve.MakeBound(0, nativeCurve.Period);
      }

      if (IsCurveClosed(nativeCurve))
      {
        var (first, second) = SplitCurveInTwoHalves(nativeCurve);
        if (edge.ProxyCurveIsReversed)
        {
          first = first.CreateReversed();
          second = second.CreateReversed();
        }
        var halfEdgeA = DB.BRepBuilderEdgeGeometry.Create(first);
        var halfEdgeB = DB.BRepBuilderEdgeGeometry.Create(second);
        return edge.ProxyCurveIsReversed
          ? new List<DB.BRepBuilderEdgeGeometry> { halfEdgeA, halfEdgeB }
          : new List<DB.BRepBuilderEdgeGeometry> { halfEdgeB, halfEdgeA };
      }

      // TODO: Remove short segments if smaller than 'Revit.ShortCurveTolerance'.
      var fullEdge = DB.BRepBuilderEdgeGeometry.Create(nativeCurve);
      return new List<DB.BRepBuilderEdgeGeometry> { fullEdge };
    }

    var iterator = edge.ProxyCurveIsReversed ? nativeCurveArray.ReverseIterator() : nativeCurveArray.ForwardIterator();

    var result = new List<DB.BRepBuilderEdgeGeometry>();
    while (iterator.MoveNext())
    {
      var crv = (DB.Curve)iterator.Current;
      if (edge.ProxyCurveIsReversed)
      {
        crv = crv.CreateReversed();
      }

      result.Add(DB.BRepBuilderEdgeGeometry.Create(crv));
    }

    return result;
  }

  private bool IsCurveClosed(DB.Curve nativeCurve, double tol = 1E-6)
  {
    var endPoint = nativeCurve.GetEndPoint(0);
    var source = nativeCurve.GetEndPoint(1);
    var distanceTo = endPoint.DistanceTo(source);
    return distanceTo < tol;
  }

  private (DB.Curve, DB.Curve) SplitCurveInTwoHalves(DB.Curve nativeCurve)
  {
    // Revit does not like single curve loop edges, so we split them in two.
    var start = nativeCurve.GetEndParameter(0);
    var end = nativeCurve.GetEndParameter(1);
    var mid = start + (end - start) / 2;

    var a = nativeCurve.Clone();
    a.MakeBound(start, mid);
    var b = nativeCurve.Clone();
    b.MakeBound(mid, end);

    return (a, b);
  }
}
