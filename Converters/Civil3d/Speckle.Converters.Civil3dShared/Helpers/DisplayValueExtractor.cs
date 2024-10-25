using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
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
  private readonly ILogger<DisplayValueExtractor> _logger;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _converterSettings;

  public DisplayValueExtractor(
    ITypedConverter<ADB.Solid3d, SOG.Mesh> solidConverter,
    ITypedConverter<CDB.TinSurface, SOG.Mesh> tinSurfaceConverter,
    ITypedConverter<CDB.GridSurface, SOG.Mesh> gridSurfaceConverter,
    ITypedConverter<AG.Point3dCollection, SOG.Polyline> pointCollectionConverter,
    ILogger<DisplayValueExtractor> logger,
    IConverterSettingsStore<Civil3dConversionSettings> converterSettings
  )
  {
    _solidConverter = solidConverter;
    _tinSurfaceConverter = tinSurfaceConverter;
    _gridSurfaceConverter = gridSurfaceConverter;
    _pointCollectionConverter = pointCollectionConverter;
    _logger = logger;
    _converterSettings = converterSettings;
  }

  public List<Base>? GetDisplayValue(CDB.Entity entity)
  {
    switch (entity)
    {
      case CDB.FeatureLine featureline:
        SOG.Polyline featurelinePolyline = _pointCollectionConverter.Convert(
          featureline.GetPoints(Autodesk.Civil.FeatureLinePointType.PIPoint)
        );
        return new() { featurelinePolyline };

      // pipe networks: https://help.autodesk.com/view/CIV3D/2025/ENU/?guid=ade47b62-debf-f899-9b94-5645a620ab4f
      case CDB.Part part:
        SOG.Mesh partMesh = _solidConverter.Convert(part.Solid3dBody);
        return new() { partMesh };

      // surfaces: https://help.autodesk.com/view/CIV3D/2025/ENU/?guid=d741aa49-e7da-9513-6b0b-226ebe3fa43f
      // POC: volume surfaces not supported
      case CDB.TinSurface tinSurface:
        SOG.Mesh tinSurfaceMesh = _tinSurfaceConverter.Convert(tinSurface);
        return new() { tinSurfaceMesh };
      case CDB.GridSurface gridSurface:
        SOG.Mesh gridSurfaceMesh = _gridSurfaceConverter.Convert(gridSurface);
        return new() { gridSurfaceMesh };

      // Corridors are complicated: their display values are extracted in the CorridorHandler when processing corridor children, since they are attached to the corridor subassemblies.
      case CDB.Corridor:
        return new();

      default:
        return null;
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
  public List<Base>? ProcessICurvesForDisplay(List<ICurve>? iCurves)
  {
    if (iCurves is null)
    {
      return null;
    }

    List<Base> result = new();
    foreach (ICurve curve in iCurves)
    {
      switch (curve)
      {
        case SOG.Line:
        case SOG.Polyline:
        case SOG.Arc:
          result.Add((Base)curve);
          break;
        case SOG.Polycurve polycurve:
          List<Base>? processedSegments = ProcessICurvesForDisplay(polycurve.segments);
          if (processedSegments is not null)
          {
            result.AddRange(processedSegments);
          }
          break;
      }
    }

    return result.Count > 0 ? result : null;
  }
}
