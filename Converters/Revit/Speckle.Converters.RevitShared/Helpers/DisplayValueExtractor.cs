using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.Helpers;

// POC: needs breaking down https://spockle.atlassian.net/browse/CNX-9354
public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<
    (Dictionary<DB.ElementId, List<DB.Mesh>> target, DB.ElementId parentElementId, bool makeTransparent),
    List<SOG.Mesh>
  > _meshByMaterialConverter;

  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;
  private readonly ITypedConverter<DB.PolyLine, SOG.Polyline> _polylineConverter;
  private readonly ITypedConverter<DB.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<DB.PointCloudInstance, SOG.Pointcloud> _pointcloudConverter;
  private readonly ILogger<DisplayValueExtractor> _logger;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public DisplayValueExtractor(
    ITypedConverter<
      (Dictionary<DB.ElementId, List<DB.Mesh>> target, DB.ElementId parentElementId, bool makeTransparent),
      List<SOG.Mesh>
    > meshByMaterialConverter,
    ITypedConverter<DB.Curve, ICurve> curveConverter,
    ITypedConverter<DB.PolyLine, SOG.Polyline> polylineConverter,
    ITypedConverter<DB.Point, SOG.Point> pointConverter,
    ITypedConverter<DB.PointCloudInstance, SOG.Pointcloud> pointcloudConverter,
    ILogger<DisplayValueExtractor> logger,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _meshByMaterialConverter = meshByMaterialConverter;
    _curveConverter = curveConverter;
    _polylineConverter = polylineConverter;
    _pointConverter = pointConverter;
    _pointcloudConverter = pointcloudConverter;
    _logger = logger;
    _converterSettings = converterSettings;
  }

  public List<Base> GetDisplayValue(DB.Element element)
  {
    switch (element)
    {
      // get custom (anything not using element.get_geometry) display values
      case DB.PointCloudInstance pointcloud:
        return new() { _pointcloudConverter.Convert(pointcloud) };
      case DB.ModelCurve modelCurve:
        return new() { GetCurveDisplayValue(modelCurve.GeometryCurve) };
      case DB.Grid grid:
        return new() { GetCurveDisplayValue(grid.Curve) };
      case DB.Area area:
        List<Base> areaDisplay = new();
        using (var options = new DB.SpatialElementBoundaryOptions())
        {
          foreach (IList<DB.BoundarySegment> boundarySegmentGroup in area.GetBoundarySegments(options))
          {
            foreach (DB.BoundarySegment boundarySegment in boundarySegmentGroup)
            {
              areaDisplay.Add(GetCurveDisplayValue(boundarySegment.GetCurve()));
            }
          }
        }

        return areaDisplay;

      // handle specific types of objects with multiple parts or children
      // curtain and stacked walls should have their display values in their children
      case DB.Wall wall:
        return wall.CurtainGrid is not null || wall.IsStackedWall ? new() : GetGeometryDisplayValue(element);
      // railings should also include toprail which need to be retrieved separately
      case DBA.Railing railing:
        List<Base> railingDisplay = GetGeometryDisplayValue(railing);
        if (railing.TopRail != DB.ElementId.InvalidElementId)
        {
          var topRail = _converterSettings.Current.Document.GetElement(railing.TopRail);
          railingDisplay.AddRange(GetGeometryDisplayValue(topRail));
        }

        return railingDisplay;

      // POC: footprint roofs can have curtain walls in them. Need to check if they can also have non-curtain wall parts, bc currently not skipping anything.
      // case DB.FootPrintRoof footPrintRoof:

      default:
        return GetGeometryDisplayValue(element);
    }
  }

  private Base GetCurveDisplayValue(DB.Curve curve) => (Base)_curveConverter.Convert(curve);

  private List<Base> GetGeometryDisplayValue(DB.Element element, DB.Options? options = null)
  {
    List<Base> displayValue = new();
    var (solids, meshes, curves, polylines, points) = GetSortedGeometryFromElement(element, options);

    // handle all solids and meshes by their material
    var meshesByMaterial = GetMeshesByMaterial(meshes, solids);
    List<SOG.Mesh> displayMeshes = _meshByMaterialConverter.Convert(
      (meshesByMaterial, element.Id, ShouldSetElementDisplayToTransparent(element))
    );
    displayValue.AddRange(displayMeshes);

    // add rest of geometry
    foreach (var curve in curves)
    {
      displayValue.Add(GetCurveDisplayValue(curve));
    }

    foreach (var polyline in polylines)
    {
      displayValue.Add(_polylineConverter.Convert(polyline));
    }

    foreach (var point in points)
    {
      displayValue.Add(_pointConverter.Convert(point));
    }

    return displayValue;
  }

  private static Dictionary<DB.ElementId, List<DB.Mesh>> GetMeshesByMaterial(
    List<DB.Mesh> meshes,
    List<DB.Solid> solids
  )
  {
    var meshesByMaterial = new Dictionary<DB.ElementId, List<DB.Mesh>>();
    foreach (var mesh in meshes)
    {
      var materialId = mesh.MaterialElementId;
      if (!meshesByMaterial.TryGetValue(materialId, out List<DB.Mesh>? value))
      {
        value = new List<DB.Mesh>();
        meshesByMaterial[materialId] = value;
      }

      value.Add(mesh);
    }

    foreach (var solid in solids)
    {
      foreach (DB.Face face in solid.Faces)
      {
        var materialId = face.MaterialElementId;
        if (!meshesByMaterial.TryGetValue(materialId, out List<DB.Mesh>? value))
        {
          value = new List<DB.Mesh>();
          meshesByMaterial[materialId] = value;
        }

        var mesh = face.Triangulate(); //Revit API can return null here
        if (mesh is null)
        {
          continue;
        }

        value.Add(mesh);
      }
    }

    return meshesByMaterial;
  }

  // We do not handle DetailLevelType.Undefined behavior, so we don't use 'DB.ViewDetailLevel' enum directly as option in UI.
  private readonly Dictionary<DetailLevelType, DB.ViewDetailLevel> _detailLevelMap =
    new()
    {
      { DetailLevelType.Coarse, DB.ViewDetailLevel.Coarse },
      { DetailLevelType.Medium, DB.ViewDetailLevel.Medium },
      { DetailLevelType.Fine, DB.ViewDetailLevel.Fine }
    };

  private (
    List<DB.Solid>,
    List<DB.Mesh>,
    List<DB.Curve>,
    List<DB.PolyLine>,
    List<DB.Point>
  ) GetSortedGeometryFromElement(DB.Element element, DB.Options? options)
  {
    //options = ViewSpecificOptions ?? options ?? new Options() { DetailLevel = DetailLevelSetting };
    options ??= new DB.Options { DetailLevel = _detailLevelMap[_converterSettings.Current.DetailLevel] };
    options = OverrideViewOptions(element, options);

    DB.GeometryElement geom;
    try
    {
      geom = element.get_Geometry(options);
    }
    // POC: should we be trying to continue?
    catch (Autodesk.Revit.Exceptions.ArgumentException)
    {
      options.ComputeReferences = false;
      geom = element.get_Geometry(options);
    }

    List<DB.Solid> solids = new();
    List<DB.Mesh> meshes = new();
    List<DB.Curve> curves = new();
    List<DB.PolyLine> polylines = new();
    List<DB.Point> points = new();

    if (geom != null && geom.Any())
    {
      // retrieves all meshes and solids from a geometry element
      SortGeometry(element, solids, meshes, curves, polylines, points, geom);
    }

    return (solids, meshes, curves, polylines, points);
  }

  /// <summary>
  /// According to the remarks on the GeometryInstance class in the RevitAPIDocs,
  /// https://www.revitapidocs.com/2024/fe25b14f-5866-ca0f-a660-c157484c3a56.htm,
  /// a family instance geometryElement should have a top-level geometry instance when the symbol
  /// does not have modified geometry (the docs say that modified geometry will not have a geom instance,
  /// however in my experience, all family instances have a top-level geom instance, but if the family instance
  /// is modified, then the geom instance won't contain any geometry.)
  ///
  /// This remark also leads me to think that a family instance will not have top-level solids and geom instances.
  /// We are logging cases where this is not true.
  ///
  /// Note: this is basically a geometry unpacker for all types of geometry
  /// </summary>
  /// <param name="element"></param>
  /// <param name="solids"></param>
  /// <param name="meshes"></param>
  /// <param name="curves"></param>
  /// <param name="polylines"></param>
  /// <param name="points"></param>
  /// <param name="geom"></param>
  private void SortGeometry(
    DB.Element element,
    List<DB.Solid> solids,
    List<DB.Mesh> meshes,
    List<DB.Curve> curves,
    List<DB.PolyLine> polylines,
    List<DB.Point> points,
    DB.GeometryElement geom
  )
  {
    foreach (DB.GeometryObject geomObj in geom)
    {
      if (SkipGeometry(geomObj, element))
      {
        continue;
      }

      switch (geomObj)
      {
        case DB.Solid solid:
          // skip invalid solid
          if (solid.Faces.Size == 0)
          {
            continue;
          }

          solids.Add(solid);
          break;

        case DB.Mesh mesh:
          meshes.Add(mesh);
          break;

        case DB.Curve curve:
          curves.Add(curve);
          break;

        case DB.PolyLine polyline:
          polylines.Add(polyline);
          break;

        case DB.Point point:
          points.Add(point);
          break;

        case DB.GeometryInstance instance:
          // element transforms should not be carried down into nested geometryInstances.
          // Nested geomInstances should have their geom retreived with GetInstanceGeom, not GetSymbolGeom
          SortGeometry(element, solids, meshes, curves, polylines, points, instance.GetInstanceGeometry());
          break;

        case DB.GeometryElement geometryElement:
          SortGeometry(element, solids, meshes, curves, polylines, points, geometryElement);
          break;
      }
    }
  }

  /// <summary>
  /// We're caching a dictionary of graphic styles and their ids as it can be a costly operation doing Document.GetElement(solid.GraphicsStyleId) for every solid
  /// </summary>
  private readonly Dictionary<string, DB.GraphicsStyle> _graphicStyleCache = new();

  private bool SkipGeometry(DB.GeometryObject geomObj, DB.Element element)
  {
    if (geomObj.GraphicsStyleId == DB.ElementId.InvalidElementId)
    {
      return false; // exit fast on a potential hot path
    }

    DB.GraphicsStyle? bjk = null; // ask ogu why this variable is named like this

    if (!_graphicStyleCache.ContainsKey(geomObj.GraphicsStyleId.ToString().NotNull()))
    {
      bjk = (DB.GraphicsStyle)element.Document.GetElement(geomObj.GraphicsStyleId);
      _graphicStyleCache[geomObj.GraphicsStyleId.ToString().NotNull()] = bjk;
    }
    else
    {
      bjk = _graphicStyleCache[geomObj.GraphicsStyleId.ToString().NotNull()];
    }

#if REVIT2023_OR_GREATER
    if (bjk?.GraphicsStyleCategory.BuiltInCategory == DB.BuiltInCategory.OST_LightingFixtureSource)
    {
      return true;
    }
#else
    if (bjk?.GraphicsStyleCategory.Id.IntegerValue == (int)DB.BuiltInCategory.OST_LightingFixtureSource)
    {
      return true;
    }
#endif

    return false;
  }

  // Determines if an element should be sent with invisible display values
  private bool ShouldSetElementDisplayToTransparent(DB.Element element)
  {
#if REVIT2023_OR_GREATER
    switch (element.Category?.BuiltInCategory)
    {
      case DB.BuiltInCategory.OST_Rooms:
        return true;

      default:
        return false;
    }
#else
    return false;
#endif
  }

  /// <summary>
  /// Overrides current view options to extract meaningful geometry for various elements. E.g., pipes, plumbing fixtures, steel elements
  /// </summary>
  /// <param name="element"></param>
  /// <returns></returns>
  private DB.Options OverrideViewOptions(DB.Element element, DB.Options currentOptions)
  {
    // there is no point to progress if element category already null
    if (element.Category is null)
    {
      return currentOptions;
    }

    var elementBuiltInCategory = element.Category.GetBuiltInCategory();

    // Note: some elements do not get display values (you get invalid solids) unless we force the view detail level to be fine. This is annoying, but it's bad ux: people think the
    // elements are not there (they are, just invisible).
    if (
      elementBuiltInCategory == DB.BuiltInCategory.OST_PipeFitting
      || elementBuiltInCategory == DB.BuiltInCategory.OST_PipeAccessory
      || elementBuiltInCategory == DB.BuiltInCategory.OST_PlumbingFixtures
#if REVIT2024_OR_GREATER
      || element is DB.Toposolid // note, brought back from 2.x.x.
#endif
    )
    {
      currentOptions.DetailLevel = DB.ViewDetailLevel.Fine; // Force detail level to be fine
      return currentOptions;
    }

    // NOTE: On steel elements. This is an incomplete solution.
    // If steel element proxies will be sucked in via category selection, and they are not visible in the current view, they will not be extracted out.
    // I'm inclined to go with this as a semi-permanent limitation. See:
    // https://speckle.community/t/revit-2025-2-missing-elements-and-colors/14073
    // and https://forums.autodesk.com/t5/revit-api-forum/how-to-get-steelproxyelement-geometry/td-p/10347898
    if (
      elementBuiltInCategory
      is DB.BuiltInCategory.OST_StructConnections
        or DB.BuiltInCategory.OST_StructConnectionPlates
        or DB.BuiltInCategory.OST_StructuralFraming
        or DB.BuiltInCategory.OST_StructuralColumns
        or DB.BuiltInCategory.OST_StructConnectionBolts
        or DB.BuiltInCategory.OST_StructConnectionWelds
        or DB.BuiltInCategory.OST_StructConnectionShearStuds
    )
    {
      // try-catch is not pretty. we need to understand this better.
      try
      {
        // try to create options with the active view - this will work for the main document and will fail with the linked models. Well, we can safely swallow the exception since we do not care DB.Options for linked models.
        return new DB.Options() { View = _converterSettings.Current.Document.NotNull().ActiveView };
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // if that fails (which will happen for linked documents), use the current options
        return currentOptions;
      }
    }
    return currentOptions;
  }
}
