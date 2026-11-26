using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared.Helpers;

public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<ADB.Solid3d, SOG.Mesh> _solidConverter;
  private readonly ITypedConverter<CDB.TinSurface, SOG.Mesh> _tinSurfaceConverter;
  private readonly ITypedConverter<CDB.GridSurface, SOG.Mesh> _gridSurfaceConverter;
  private readonly ITypedConverter<AG.Point3dCollection, SOG.Polyline> _pointCollectionConverter;
  private readonly ITypedConverter<ADB.Curve, ICurve> _curveConverter;

  public DisplayValueExtractor(
    ITypedConverter<ADB.Solid3d, SOG.Mesh> solidConverter,
    ITypedConverter<CDB.TinSurface, SOG.Mesh> tinSurfaceConverter,
    ITypedConverter<CDB.GridSurface, SOG.Mesh> gridSurfaceConverter,
    ITypedConverter<AG.Point3dCollection, SOG.Polyline> pointCollectionConverter,
    ITypedConverter<ADB.Curve, ICurve> curveConverter
  )
  {
    _solidConverter = solidConverter;
    _tinSurfaceConverter = tinSurfaceConverter;
    _gridSurfaceConverter = gridSurfaceConverter;
    _pointCollectionConverter = pointCollectionConverter;
    _curveConverter = curveConverter;
  }

  public IEnumerable<Base> GetDisplayValue(CDB.Entity entity)
  {
    switch (entity)
    {
      case CDB.SectionView sectionView:
        // Explode into transient objects only. The drawing's view and its annotations stay intact.
        foreach (var curve in SectionViewCurves(sectionView))
        {
          yield return curve;
        }
        break;
      case CDB.Profile profile:
        var profilePoints = CivilLinearGeometry.ProfilePoints(profile);
        if (profilePoints.Length > 1)
        {
          yield return _pointCollectionConverter.Convert(new AG.Point3dCollection(profilePoints));
        }
        break;

      case CDB.Section:
        // Native probes show SectionPoints.Location is (offset, elevation, 0), not WCS.
        // Reject this conversion until a sample-line-aware transform is validated.
        throw new NotSupportedException("Section display export is quarantined: offset/elevation must be transformed along the sample line. Corridor sections require a separate geometry extractor.");

      case CDB.SampleLine sampleLine:
        var vertices = sampleLine.Vertices.Cast<CDB.SampleLineVertex>().Select(v => v.Location).ToArray();
        if (vertices.Length > 1)
        {
          yield return _pointCollectionConverter.Convert(new AG.Point3dCollection(vertices));
        }
        break;

      // POC: we are sending featurelines as approximated polylines because they are 2.5d curves:
      // they can have line or arc segments, and each vertex can have different elevations.
      // there is no native type that can capture the full 3d representation of these curves.
      // if this becomes essential, can explore a hack where each point is converted to 2d, and separate line/arc segments are calculated, and then their points readjusted with 3d z values
      // SurveyFigures inherit from featureline
      case CDB.FeatureLine featureline:
        SOG.Polyline featurelinePolyline = _pointCollectionConverter.Convert(
          featureline.GetPoints(Autodesk.Civil.FeatureLinePointType.PIPoint)
        );
        yield return featurelinePolyline;
        break;

      // pipe networks: https://help.autodesk.com/view/CIV3D/2025/ENU/?guid=ade47b62-debf-f899-9b94-5645a620ab4f
      case CDB.Part part:
        SOG.Mesh partMesh = _solidConverter.Convert(part.Solid3dBody);
        yield return partMesh;
        break;
      // pressure pipe networks: https://help.autodesk.com/view/CIV3D/2025/ENU/?guid=f1361ca3-4195-3b06-8a66-ecd31f5208b0
      case CDB.PressurePart pressurePart:
        SOG.Mesh pressurePartMesh = _solidConverter.Convert(pressurePart.Get3dBody());
        yield return pressurePartMesh;
        break;

      // surfaces: https://help.autodesk.com/view/CIV3D/2025/ENU/?guid=d741aa49-e7da-9513-6b0b-226ebe3fa43f
      // POC: volume surfaces not supported
      case CDB.TinSurface tinSurface:
        SOG.Mesh tinSurfaceMesh = _tinSurfaceConverter.Convert(tinSurface);
        yield return tinSurfaceMesh;
        break;
      case CDB.GridSurface gridSurface:
        SOG.Mesh gridSurfaceMesh = _gridSurfaceConverter.Convert(gridSurface);
        yield return gridSurfaceMesh;
        break;

      // catchments
      case CDB.Catchment catchment:
        SOG.Polyline catchmentPolyline = _pointCollectionConverter.Convert(catchment.BoundaryPolyline3d);
        catchmentPolyline.closed = true;
        yield return catchmentPolyline;
        break;

      // Corridors are complicated: their display values are extracted in the CorridorHandler when processing corridor children, since they are attached to the corridor subassemblies.
      case CDB.Corridor:
        yield break;

      default:
        yield break;
    }
  }

  private IEnumerable<Base> SectionViewCurves(ADB.Entity entity)
  {
    using var exploded = new ADB.DBObjectCollection();
    entity.Explode(exploded);
    try
    {
      foreach (ADB.DBObject item in exploded)
      {
        if (item is ADB.Curve curve)
        {
          foreach (var display in ProcessICurvesForDisplay(new List<ICurve> { _curveConverter.Convert(curve) }))
          {
            yield return display;
          }
        }
        else if (item is ADB.BlockReference block)
        {
          foreach (var display in SectionViewCurves(block))
          {
            yield return display;
          }
        }
      }
    }
    finally
    {
      foreach (ADB.DBObject item in exploded)
      {
        item.Dispose();
      }
    }
  }

  /// <summary>
  /// Processes a list of ICurves for suitable display value curves.
  /// </summary>
  /// <param name="iCurves"></param>
  /// <returns>
  /// List of simple curves: lines, polylines, and arcs.
  /// Null if no suitable display curves were found.
  /// </returns>
  public IEnumerable<Base> ProcessICurvesForDisplay(List<ICurve>? iCurves)
  {
    if (iCurves is null)
    {
      yield break;
    }

    foreach (ICurve curve in iCurves)
    {
      switch (curve)
      {
        case SOG.Line:
        case SOG.Polyline:
        case SOG.Arc:
          yield return (Base)curve;
          break;
        case SOG.Polycurve polycurve:
          IEnumerable<Base> processedSegments = ProcessICurvesForDisplay(polycurve.segments);
          foreach (Base processedSegment in processedSegments)
          {
            yield return processedSegment;
          }
          break;
      }
    }
  }
}
