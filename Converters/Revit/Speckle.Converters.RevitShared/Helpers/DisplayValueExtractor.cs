using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.ToSpeckle;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle;
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

      // Rebar: get_Geometry() returns null, use GetTransformedCenterlineCurves/GetFullGeometryForView + apply reference point transform
      case DB.Structure.Rebar rebar:
        return _converterSettings.Current.SendRebarsAsVolumetric
          ? GetRebarVolumetricDisplayValue(rebar)
          : GetRebarCenterlineDisplayValue(rebar);

      // AreaReinforcement/PathReinforcement get_Geometry() returns curves in document coordinates
      // unlike Rebar which needs reference point transform applied, these are already correct
      case DB.Structure.AreaReinforcement:
      case DB.Structure.PathReinforcement:
        return GetAreaReinforcementDisplayValue(element);

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
        : localToDocument;

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
  /// Converts sorted geometry into DisplayValueResults <see cref="ElementTopLevelConverterToSpeckle"/>.
  /// </summary>
  /// <remarks>
  /// Applies localToWorld only to curves, points, polylines.
  /// Meshes remain in symbol space to generate correct instance proxies and avoid duplicates.
  /// </remarks>
  private List<DisplayValueResult> ProcessGeometryCollections(
    DB.Element element,
    GeometryCollections collections,
    DB.Transform? localToWorld
  )
  {
    var meshesByMaterial = GetMeshesByMaterial(collections.Meshes, collections.Solids);
    var displayMeshes = _meshByMaterialConverter.Convert(
      (meshesByMaterial, element.Id, ShouldSetElementDisplayToTransparent(element))
    );

    List<DisplayValueResult> displayValue = new(collections.TotalCount);

    foreach (var mesh in displayMeshes)
    {
      // if we have a transform, keep mesh in symbol space and attach transform
      displayValue.Add(
        localToWorld != null
          ? DisplayValueResult.WithTransform(mesh, TransformToMatrix(localToWorld))
          : DisplayValueResult.WithoutTransform(mesh)
      );
    }

    foreach (var curve in collections.Curves)
    {
      displayValue.Add(DisplayValueResult.WithoutTransform(GetCurveDisplayValue(curve)));
    }

    foreach (var polyline in collections.Polylines)
    {
      displayValue.Add(DisplayValueResult.WithoutTransform(_polylineConverter.Convert(polyline)));
    }

    foreach (var point in collections.Points)
    {
      displayValue.Add(DisplayValueResult.WithoutTransform(_pointConverter.Convert(point)));
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
  /// Sorts element geometry into solids, meshes, curves, polylines, points.
  /// </summary>
  /// <remarks>
  /// GeometryInstances are processed via GetSymbolGeometry() with accumulated transforms,
  /// keeping meshes in symbol space and avoiding double transforms.
  /// </remarks>
  private void SortGeometry(
    DB.Element element,
    GeometryCollections collections,
    DB.GeometryElement geom,
    DB.Transform? accumulatedTransform
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
          if (solid.Faces.Size == 0)
          {
            continue;
          }

          if (accumulatedTransform != null)
          {
            // apply transform to bring solid into document/world space
            // only apply once to avoid double-transform bugs
            solid = DB.SolidUtils.CreateTransformed(solid, accumulatedTransform);
          }

          collections.Solids.Add(solid);
          break;

        case DB.Mesh mesh:
          if (accumulatedTransform != null)
          {
            // apply accumulated transform to mesh
            // prevents geometry from being incorrectly transformed later [Ref: CNX-2875]
            mesh = mesh.get_Transformed(accumulatedTransform);
          }

          collections.Meshes.Add(mesh);
          break;

        case DB.Curve curve:
          if (accumulatedTransform is not null)
          {
            curve = curve.CreateTransformed(accumulatedTransform);
          }
          collections.Curves.Add(curve);
          break;

        case DB.PolyLine polyline:
          if (accumulatedTransform is not null)
          {
            var coords = polyline.GetCoordinates().Select(p => accumulatedTransform.OfPoint(p)).ToList();
            using var transformedPolyline = DB.PolyLine.Create(coords);
            collections.Polylines.Add(transformedPolyline);
          }
          else
          {
            collections.Polylines.Add(polyline);
          }
          break;

        case DB.Point point:
          if (accumulatedTransform is not null)
          {
            point = DB.Point.Create(accumulatedTransform.OfPoint(point.Coord));
          }

          collections.Points.Add(point);
          break;

        case DB.GeometryInstance instance:
          // GeometryInstance.Transform: symbol → parent coordinate system
          // multiply with accumulatedTransform to handle nested instances
          var instanceTransform = instance.Transform;
          var nextTransform =
            accumulatedTransform != null ? accumulatedTransform.Multiply(instanceTransform) : instanceTransform;

          // always use symbol geometry, never GetInstanceGeometry() [Ref: CNX-2875]
          SortGeometry(element, collections, instance.GetSymbolGeometry(), nextTransform);
          break;

        case DB.GeometryElement geometryElement:
          // raw GeometryElement: it has no transform of its own
          // pass accumulatedTransform from parent if present
          SortGeometry(element, collections, geometryElement, accumulatedTransform);
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

    DB.GraphicsStyle? bjk; // ask ogu why this variable is named like this

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

    // cable trays (and fittings) are MEP system families whose geometry detail is effectively view-driven.
    // So, we've seen that, Options.DetailLevel is ignored by get_Geometry() for these categories unless a View is
    // explicitly supplied, and Revit will always return a medium-detail representation otherwise [Ref: CNX-2735]
    // We force extraction through the active view here (if there is one!)
    if (
      elementBuiltInCategory == DB.BuiltInCategory.OST_CableTray
      || elementBuiltInCategory == DB.BuiltInCategory.OST_CableTrayFitting
    )
    {
      try
      {
        return new DB.Options { View = _converterSettings.Current.Document.NotNull().ActiveView };
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // linked docs or invalid view context – fall back to non-view-specific options
        return currentOptions;
      }
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
      DB.Transform? documentToWorld = _converterSettings.Current.ReferencePointTransform?.Inverse;
      SortGeometry(rebar, collections, geometryElements, null);
      return ProcessGeometryCollections(rebar, collections, documentToWorld);
    }

    // Return empty list if no geometry is found - imo not critical
    return new List<DisplayValueResult>();
  }

  /// <summary>
  /// Gets the centerline representation of a rebar element.
  /// </summary>
  /// <remarks>
  /// This method extracts the centerlines of rebar elements when a simplified representation is preferred.
  /// GetTransformedCenterlineCurves already returns curves in document coordinates. Applying any additional transform
  /// here will result in double transforms, especially for linked models.
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
      // see remarks above method for explanation
      displayValue.Add(DisplayValueResult.WithoutTransform(GetCurveDisplayValue(curve)));
    }

    return displayValue;
  }

  /// <summary>
  /// Gets display value for AreaReinforcement and PathReinforcement.
  /// </summary>
  /// <remarks>
  /// These elements' get_Geometry() returns curves already in document coordinates.
  /// Unlike Rebar.GetTransformedCenterlineCurves() which requires reference point transform,
  /// these curves should not be transformed - they're already in the correct space.
  /// </remarks>
  private List<DisplayValueResult> GetAreaReinforcementDisplayValue(DB.Element element)
  {
    var collections = GetSortedGeometryFromElement(element, null, null);
    // pass null for transform - curves are already in correct document coordinates
    return ProcessGeometryCollections(element, collections, null);
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
