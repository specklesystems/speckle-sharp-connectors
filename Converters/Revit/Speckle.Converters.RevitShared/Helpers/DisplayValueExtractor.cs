using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.ToSpeckle;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
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

  private readonly IScalingServiceToSpeckle _toSpeckleScalingService;
  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;
  private readonly ITypedConverter<DB.PolyLine, SOG.Polyline> _polylineConverter;
  private readonly ITypedConverter<DB.Point, SOG.Point> _pointConverter;
  private readonly ITypedConverter<DB.PointCloudInstance, SOG.Pointcloud> _pointcloudConverter;
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
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    IScalingServiceToSpeckle toSpeckleScalingService
  )
  {
    _meshByMaterialConverter = meshByMaterialConverter;
    _curveConverter = curveConverter;
    _polylineConverter = polylineConverter;
    _pointConverter = pointConverter;
    _pointcloudConverter = pointcloudConverter;
    _converterSettings = converterSettings;
    _toSpeckleScalingService = toSpeckleScalingService;
  }

  public List<DisplayValueResult> GetDisplayValue(DB.Element element)
  {
    switch (element)
    {
      // get custom (anything not using element.get_geometry) display values
      case DB.PointCloudInstance pointcloud:
        return [DisplayValueResult.WithoutTransform(_pointcloudConverter.Convert(pointcloud))];
      case DB.ModelCurve modelCurve:
        return [DisplayValueResult.WithoutTransform(GetCurveDisplayValue(modelCurve.GeometryCurve))];
      case DB.Grid grid:
        return [DisplayValueResult.WithoutTransform(GetCurveDisplayValue(grid.Curve))];
      case DB.Area area:
        List<DisplayValueResult> areaDisplay = new();
        using (var options = new DB.SpatialElementBoundaryOptions())
        {
          foreach (IList<DB.BoundarySegment> boundarySegmentGroup in area.GetBoundarySegments(options))
          {
            foreach (DB.BoundarySegment boundarySegment in boundarySegmentGroup)
            {
              areaDisplay.Add(DisplayValueResult.WithoutTransform(GetCurveDisplayValue(boundarySegment.GetCurve())));
            }
          }
        }
        return areaDisplay;

      // NOTE: this is only for Rebar and not AreaReinforcement, RebarInSystem
      // AreaReinforcement and RebarInSystem pass through GetGeometryDisplayValue which get DisplayValues as per hostApp
      // Rebar elements need special handling as get_Geometry() doesn't work properly
      // We either represent them as centerlines or as solids based on settings
      case DB.Structure.Rebar rebar:
        return _converterSettings.Current.SendRebarsAsVolumetric
          ? GetRebarVolumetricDisplayValue(rebar)
          : GetRebarCenterlineDisplayValue(rebar);

      // handle specific types of objects with multiple parts or children
      // curtain and stacked walls should have their display values in their children
      case DB.Wall wall:
        return wall.CurtainGrid is not null || wall.IsStackedWall ? new() : GetGeometryDisplayValue(element);

      // POC: footprint roofs can have curtain walls in them. Need to check if they can also have non-curtain wall parts, bc currently not skipping anything.
      // case DB.FootPrintRoof footPrintRoof:

      default:
        return GetGeometryDisplayValue(element);
    }
  }

  private Base GetCurveDisplayValue(DB.Curve curve) => (Base)_curveConverter.Convert(curve);

  private List<DisplayValueResult> GetGeometryDisplayValue(DB.Element element, DB.Options? options = null)
  {
    using DB.Transform? localToDocument = GetTransform(element);
    using DB.Transform? documentToLocal = localToDocument?.Inverse;

    DB.Transform? documentToWorld = _converterSettings.Current.ReferencePointTransform?.Inverse;
    using DB.Transform? compoundTransform =
      localToDocument is not null && documentToWorld is not null
        ? documentToWorld.Multiply(localToDocument)
        : localToDocument; // don't want to accidentally dispose of the ReferencePointTransform

    DB.Transform? localToWorld = compoundTransform ?? documentToWorld;

    var collections = GetSortedGeometryFromElement(element, options, documentToLocal);
    return ProcessGeometryCollections(element, collections, localToWorld);
  }

  /// <summary>
  /// Extracts and sorts all geometry from an element into separate collections by type.
  /// </summary>
  /// <remarks>
  /// Extraction of geometry from any element using get_Geometry().
  /// Note: Some special element types (like Rebar) cannot use this method as their
  /// get_Geometry() returns null, requiring specialized extraction methods.
  /// </remarks>
  /// <param name="element"></param>
  /// <param name="options"></param>
  /// <param name="worldToLocal"></param>
  /// <returns></returns>
  private GeometryCollections GetSortedGeometryFromElement(
    DB.Element element,
    DB.Options? options,
    DB.Transform? worldToLocal
  )
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

    var collections = new GeometryCollections();

    if (geom != null && geom.Any())
    {
      // retrieves all meshes and solids from a geometry element
      SortGeometry(element, collections, geom, worldToLocal);
    }

    return collections;
  }

  /// <summary>
  /// Processes collections of different geometry types and converts them to display values.
  /// Extracted as a common method to reduce code duplication between regular geometry processing and special cases like rebar.
  /// </summary>
  /// <remarks>
  /// Essentially all the ensuing steps after the common get_Geometry element method
  /// </remarks>
  private List<DisplayValueResult> ProcessGeometryCollections(
    DB.Element element,
    GeometryCollections collections,
    DB.Transform? localToWorld
  )
  {
    // handle all solids and meshes by their material
    var meshesByMaterial = GetMeshesByMaterial(collections.Meshes, collections.Solids);
    List<SOG.Mesh> displayMeshes = _meshByMaterialConverter.Convert(
      (meshesByMaterial, element.Id, ShouldSetElementDisplayToTransparent(element))
    );

    List<DisplayValueResult> displayValue = new(collections.TotalCount);
    Matrix4x4? matrix = localToWorld is not null ? TransformToMatrix(localToWorld) : null;

    foreach (SOG.Mesh mesh in displayMeshes)
    {
      displayValue.Add(
        matrix.HasValue
          ? DisplayValueResult.WithTransform(mesh, matrix.Value)
          : DisplayValueResult.WithoutTransform(mesh)
      );
    }

    // transform curves, polylines, and points to world coordinates before conversion.
    // Unlike meshes/solids which are proxified with transform matrices, these geometry
    // types must have their final world coordinates baked directly into their geometry.
    foreach (var curve in collections.Curves)
    {
      if (localToWorld is not null)
      {
        using var transformedCurve = curve.CreateTransformed(localToWorld);
        displayValue.Add(DisplayValueResult.WithoutTransform(GetCurveDisplayValue(transformedCurve)));
      }
      else
      {
        displayValue.Add(DisplayValueResult.WithoutTransform(GetCurveDisplayValue(curve)));
      }
    }

    // Note: Creating new polyline/point instances for transformation isn't ideal for perf,
    // but Revit API doesn't provide in-place transform methods. Trade-off is acceptable since
    // family instances typically don't have massive numbers of raw polylines/points in their geometry.
    foreach (var polyline in collections.Polylines)
    {
      if (localToWorld is not null)
      {
        var coords = polyline.GetCoordinates();
        var transformedCoords = coords.Select(coord => localToWorld.OfPoint(coord)).ToList();
        using var transformedPolyline = DB.PolyLine.Create(transformedCoords);
        displayValue.Add(DisplayValueResult.WithoutTransform(_polylineConverter.Convert(transformedPolyline)));
      }
      else
      {
        displayValue.Add(DisplayValueResult.WithoutTransform(_polylineConverter.Convert(polyline)));
      }
    }

    foreach (var point in collections.Points)
    {
      if (localToWorld is not null)
      {
        using var transformedPoint = DB.Point.Create(localToWorld.OfPoint(point.Coord));
        displayValue.Add(DisplayValueResult.WithoutTransform(_pointConverter.Convert(transformedPoint)));
      }
      else
      {
        displayValue.Add(DisplayValueResult.WithoutTransform(_pointConverter.Convert(point)));
      }
    }

    return displayValue;
  }

  private Matrix4x4 TransformToMatrix(DB.Transform transform) =>
    new()
    {
      M11 = transform.BasisX.X,
      M21 = transform.BasisX.Y,
      M31 = transform.BasisX.Z,
      M41 = 0,

      M12 = transform.BasisY.X,
      M22 = transform.BasisY.Y,
      M32 = transform.BasisY.Z,
      M42 = 0,

      M13 = transform.BasisZ.X,
      M23 = transform.BasisZ.Y,
      M33 = transform.BasisZ.Z,
      M43 = 0,

      M14 = _toSpeckleScalingService.ScaleLength(transform.Origin.X),
      M24 = _toSpeckleScalingService.ScaleLength(transform.Origin.Y),
      M34 = _toSpeckleScalingService.ScaleLength(transform.Origin.Z),
      M44 = 1
    };

  private static DB.Transform? GetTransform(DB.Element element)
  {
    if (element is DB.Instance i)
    {
      return i.GetTotalTransform();
    }

    return null;
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
  private void SortGeometry(
    DB.Element element,
    GeometryCollections collections,
    DB.GeometryElement geom,
    DB.Transform? worldToLocal
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

          if (worldToLocal is not null)
          {
            solid = DB.SolidUtils.CreateTransformed(solid, worldToLocal);
          }
          collections.Solids.Add(solid);
          break;

        case DB.Mesh mesh:
          if (worldToLocal is not null)
          {
            mesh = mesh.get_Transformed(worldToLocal);
          }
          collections.Meshes.Add(mesh);
          break;

        // curves, polylines, and points are transformed to world space in ProcessGeometryCollections,
        // not here, because they cannot be proxified like meshes.
        case DB.Curve curve:
          collections.Curves.Add(curve);
          break;

        case DB.PolyLine polyline:
          collections.Polylines.Add(polyline);
          break;

        case DB.Point point:
          collections.Points.Add(point);
          break;

        case DB.GeometryInstance instance:
          // element transforms should not be carried down into nested geometryInstances.
          // Nested geomInstances should have their geom retrieved with GetInstanceGeom, not GetSymbolGeom
          if (worldToLocal == null) //see remark on method for why this is safe to do...
          {
            SortGeometry(element, collections, instance.GetInstanceGeometry(), null);
          }
          else
          {
            SortGeometry(element, collections, instance.GetSymbolGeometry(), null);
          }
          break;

        case DB.GeometryElement geometryElement:
          SortGeometry(element, collections, geometryElement, null);
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

  /// <summary>
  /// Gets the solid representation of rebar elements.
  /// </summary>
  /// <remarks>
  /// Rebars require special handling since the standard get_Geometry() method returns null.
  /// Instead, we use GetFullGeometryForView() to obtain the geometry and then process it
  /// using the standard geometry sorting and conversion.
  /// </remarks>
  private List<DisplayValueResult> GetRebarVolumetricDisplayValue(DB.Structure.Rebar rebar)
  {
    var collections = new GeometryCollections();

    // Regular get_Geometry() returns null for rebar, so we need to use GetFullGeometryForView
    // ❗NOTE: ️view detail level needs to be fine in order for this to work
    // Same behaviour as sending structural frame though - consistent and therefore okay.
    DB.GeometryElement? geometryElements = rebar.GetFullGeometryForView(_converterSettings.Current.Document.ActiveView);

    if (geometryElements != null)
    {
      SortGeometry(rebar, collections, geometryElements, null);
      return ProcessGeometryCollections(rebar, collections, null);
    }

    // Return empty list if no geometry is found - imo not critical
    return new List<DisplayValueResult>();
  }

  /// <summary>
  /// Gets the centerline representation of a rebar element.
  /// </summary>
  /// <remarks>
  /// This method extracts the centerlines of rebar elements when a simplified representation is preferred.
  /// </remarks>
  private List<DisplayValueResult> GetRebarCenterlineDisplayValue(DB.Structure.Rebar rebar)
  {
    bool isSingleLayout = rebar.LayoutRule == DB.Structure.RebarLayoutRule.Single;
    int numberOfBarPositions = rebar.NumberOfBarPositions;
    List<DB.Curve> curves = new();

    for (int barPositionIndex = 0; barPositionIndex < numberOfBarPositions; barPositionIndex++)
    {
      if (!isSingleLayout)
      {
        if (
          !rebar.IncludeFirstBar && barPositionIndex == 0
          || !rebar.IncludeLastBar && barPositionIndex == rebar.NumberOfBarPositions - 1
        )
        {
          continue;
        }
      }
      curves.AddRange(
        rebar.GetTransformedCenterlineCurves(
          false,
          false,
          false,
          DB.Structure.MultiplanarOption.IncludeAllMultiplanarCurves,
          barPositionIndex
        )
      );
    }

    List<DisplayValueResult> displayValue = new();
    foreach (var curve in curves)
    {
      displayValue.Add(DisplayValueResult.WithoutTransform(GetCurveDisplayValue(curve)));
    }

    return displayValue;
  }

  /// <summary>
  /// Represents sorted collections of different geometry types extracted from an element.
  /// Used to pass multiple geometry collections as a single parameter to improve code readability
  /// and reduce the risk of parameter ordering errors.
  /// </summary>
  /// <remarks>
  /// <see cref="Solids"/> and <see cref="Meshes"/> are transformed to local coordinate space in SortGeometry.
  /// <see cref="Curves"/>, <see cref="Polylines"/>, and <see cref="Points"/> remain in their original coordinate space
  /// and are transformed to world space during processing in ProcessGeometryCollections.
  /// </remarks>
  private sealed record GeometryCollections
  {
    public List<DB.Solid> Solids { get; } = new();
    public List<DB.Mesh> Meshes { get; } = new();
    public List<DB.Curve> Curves { get; } = new();
    public List<DB.PolyLine> Polylines { get; } = new();
    public List<DB.Point> Points { get; } = new();

    public int TotalCount => Solids.Count + Meshes.Count + Curves.Count + Polylines.Count + Points.Count;
  }
}
