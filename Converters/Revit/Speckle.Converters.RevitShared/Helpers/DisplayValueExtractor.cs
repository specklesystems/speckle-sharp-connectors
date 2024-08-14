using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.Helpers;

// POC: needs breaking down https://spockle.atlassian.net/browse/CNX-9354
public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<Dictionary<DB.ElementId, List<DB.Mesh>>, List<SOG.Mesh>> _meshByMaterialConverter;
  private readonly ILogger<DisplayValueExtractor> _logger;

  public DisplayValueExtractor(
    ITypedConverter<Dictionary<DB.ElementId, List<DB.Mesh>>, List<SOG.Mesh>> meshByMaterialConverter,
    ILogger<DisplayValueExtractor> logger
  )
  {
    _meshByMaterialConverter = meshByMaterialConverter;
    _logger = logger;
  }

  public List<SOG.Mesh> GetDisplayValue(DB.Element element, DB.Options? options = null)
  {
    var (solids, meshes) = GetSolidsAndMeshesFromElement(element, options);

    var meshesByMaterial = GetMeshesByMaterial(meshes, solids);

    return _meshByMaterialConverter.Convert(meshesByMaterial);
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

        value.Add(face.Triangulate());
      }
    }

    return meshesByMaterial;
  }

  private (List<DB.Solid>, List<DB.Mesh>) GetSolidsAndMeshesFromElement(DB.Element element, DB.Options? options)
  {
    //options = ViewSpecificOptions ?? options ?? new Options() { DetailLevel = DetailLevelSetting };
    options ??= new DB.Options { DetailLevel = DB.ViewDetailLevel.Fine };

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
      // POC: switch could possibly become factory and IIndex<,> pattern and move conversions to
      // separate IComeConversionInterfaces
      switch (geomObj)
      {
        case DB.Solid solid:
          // skip invalid solid
          if (
            solid.Faces.Size == 0
            || Math.Abs(solid.SurfaceArea) == 0
            || IsSkippableGraphicStyle(solid.GraphicsStyleId, element.Document)
          )
          {
            continue;
          }

          solids.Add(solid);
          break;
        case DB.Mesh mesh:
          if (IsSkippableGraphicStyle(mesh.GraphicsStyleId, element.Document))
          {
            continue;
          }
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

  // POC: should be hoovered up with the new reporting, logging, exception philosophy
  private void LogInstanceMeshRetrievalWarnings(
    DB.Element element,
    int topLevelSolidsCount,
    int topLevelMeshesCount,
    int topLevelGeomElementCount,
    bool hasSymbolGeom
  )
  {
    if (hasSymbolGeom)
    {
      if (topLevelSolidsCount > 0)
      {
        _logger.LogWarning(
          $"Element of type {element.GetType()} with uniqueId {element.UniqueId} has valid symbol geometry and {topLevelSolidsCount} top level solids. See comment on method SortInstanceGeometry for link to RevitAPI docs that leads us to believe this shouldn't happen"
        );
      }
      if (topLevelMeshesCount > 0)
      {
        _logger.LogWarning(
          $"Element of type {element.GetType()} with uniqueId {element.UniqueId} has valid symbol geometry and {topLevelMeshesCount} top level meshes. See comment on method SortInstanceGeometry for link to RevitAPI docs that leads us to believe this shouldn't happen"
        );
      }
      if (topLevelGeomElementCount > 0)
      {
        _logger.LogWarning(
          $"Element of type {element.GetType()} with uniqueId {element.UniqueId} has valid symbol geometry and {topLevelGeomElementCount} top level geometry elements. See comment on method SortInstanceGeometry for link to RevitAPI docs that leads us to believe this shouldn't happen"
        );
      }
    }
  }

  /// <summary>
  /// We're caching a dictionary of graphic styles and their ids as it can be a costly operation doing Document.GetElement(solid.GraphicsStyleId) for every solid
  /// </summary>
  private readonly Dictionary<string, DB.GraphicsStyle> _graphicStyleCache = new();

  /// <summary>
  /// Exclude light source cones and potentially other geometries by their graphic style
  /// </summary>
  /// <param name="id"></param>
  /// <param name="doc"></param>
  /// <returns></returns>
  private bool IsSkippableGraphicStyle(DB.ElementId id, DB.Document doc)
  {
    var key = id.ToString().NotNull();
    if (_graphicStyleCache.TryGetValue(key, out var graphicStyle))
    {
      graphicStyle = (DB.GraphicsStyle)doc.GetElement(id);
      _graphicStyleCache.Add(key, graphicStyle);
    }

    if (
      graphicStyle != null
      && graphicStyle.GraphicsStyleCategory.Id.IntegerValue == (int)DB.BuiltInCategory.OST_LightingFixtureSource
    )
    {
      return true;
    }

    return false;
  }
}
