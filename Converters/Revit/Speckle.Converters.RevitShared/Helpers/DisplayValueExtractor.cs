using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.Helpers;

// POC: needs breaking down https://spockle.atlassian.net/browse/CNX-9354
public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<
    (Dictionary<DB.ElementId, List<DB.Mesh>> target, DB.ElementId parentElementId, bool makeTransparent),
    List<SOG.Mesh>
  > _meshByMaterialConverter;
  private readonly ILogger<DisplayValueExtractor> _logger;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public DisplayValueExtractor(
    ITypedConverter<
      (Dictionary<DB.ElementId, List<DB.Mesh>> target, DB.ElementId parentElementId, bool makeTransparent),
      List<SOG.Mesh>
    > meshByMaterialConverter,
    ILogger<DisplayValueExtractor> logger,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _meshByMaterialConverter = meshByMaterialConverter;
    _logger = logger;
    _converterSettings = converterSettings;
  }

  public List<SOG.Mesh> GetDisplayValue(DB.Element element, DB.Options? options = null)
  {
    var (solids, meshes) = GetSolidsAndMeshesFromElement(element, options);

    var meshesByMaterial = GetMeshesByMaterial(meshes, solids);

    List<SOG.Mesh> displayMeshes = _meshByMaterialConverter.Convert(
      (meshesByMaterial, element.Id, ShouldSetElementDisplayToTransparent(element))
    );

    return displayMeshes;
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

  private (List<DB.Solid>, List<DB.Mesh>) GetSolidsAndMeshesFromElement(DB.Element element, DB.Options? options)
  {
    //options = ViewSpecificOptions ?? options ?? new Options() { DetailLevel = DetailLevelSetting };
    options ??= new DB.Options { DetailLevel = _detailLevelMap[_converterSettings.Current.DetailLevel] };

    // Note: some elements do not get display values (you get invalid solids) unless we force the view detail level to be fine. This is annoying, but it's bad ux: people think the
    // elements are not there (they are, just invisible).
    if (
      element.Category is not null
      && (
        element.Category.GetBuiltInCategory() == DB.BuiltInCategory.OST_PipeFitting
        || element.Category.GetBuiltInCategory() == DB.BuiltInCategory.OST_PipeAccessory
        || element.Category.GetBuiltInCategory() == DB.BuiltInCategory.OST_PlumbingFixtures
#if REVIT2024_OR_GREATER
        || element is DB.Toposolid // note, brought back from 2.x.x.
#endif
      )
    )
    {
      options.DetailLevel = DB.ViewDetailLevel.Fine;
    }

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

    var solids = new List<DB.Solid>();
    var meshes = new List<DB.Mesh>();

    if (geom != null)
    {
      // retrieves all meshes and solids from a geometry element
      SortGeometry(element, solids, meshes, geom);
    }

    return (solids, meshes);
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
  /// <param name="geom"></param>
  private void SortGeometry(DB.Element element, List<DB.Solid> solids, List<DB.Mesh> meshes, DB.GeometryElement geom)
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
        case DB.GeometryInstance instance:
          // element transforms should not be carried down into nested geometryInstances.
          // Nested geomInstances should have their geom retreived with GetInstanceGeom, not GetSymbolGeom
          SortGeometry(element, solids, meshes, instance.GetInstanceGeometry());
          break;
        case DB.GeometryElement geometryElement:
          SortGeometry(element, solids, meshes, geometryElement);
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
}
