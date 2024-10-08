using Speckle.Converters.Common;
using Speckle.Converters.Common.FileOps;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Revit2023.ToHost.Raw.Geometry;

public class IRawEncodedObjectConverter : ITypedConverter<SOG.IRawEncodedObject, List<DB.GeometryObject>>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _settings;
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;

  public IRawEncodedObjectConverter(
    IConverterSettingsStore<RevitConversionSettings> settings,
    RevitToHostCacheSingleton revitToHostCacheSingleton
  )
  {
    _settings = settings;
    _revitToHostCacheSingleton = revitToHostCacheSingleton;
  }

  public List<DB.GeometryObject> Convert(SOG.IRawEncodedObject target)
  {
    var targetAsBase = (Base)target;
    var raw = target.encodedValue.contents;
    var bytes = System.Convert.FromBase64String(raw!);
    var filePath = TempFileProvider.GetTempFile("RevitX", target.encodedValue.format);
    File.WriteAllBytes(filePath, bytes);

    using var importer = new DB.ShapeImporter();
    var shapeImportResult = importer.Convert(_settings.Current.Document, filePath);

    DB.ElementId materialId = DB.ElementId.InvalidElementId;
    if (
      _revitToHostCacheSingleton.MaterialsByObjectId.TryGetValue(targetAsBase.applicationId ?? targetAsBase.id, out var mappedElementId)
    )
    {
      materialId = mappedElementId;
    }
    
    if (materialId == DB.ElementId.InvalidElementId)
    {
      return shapeImportResult.ToList(); // exit fast if there's no material id associated with this object
    }
    
    // check if there's any fallback importer results and recreate the meshes with the correct material result
    // note: disabled mesh recreation mentioned above as it's very slow. 
    var returnList = new List<DB.GeometryObject>();
    foreach (var geometryObject in shapeImportResult)
    {
      if (geometryObject is DB.Mesh mesh)
      {
        // returnList.AddRange(RecreateMeshWithMaterial(mesh, materialId)); // NOTE: disabled mesh recreation mentioned above as it's very slow. 
        returnList.Add(mesh);
      }
      else
      {
        returnList.Add(geometryObject);
      }
    }
    
    return returnList;
  }

  /// <summary>
  /// Note: this is not used as it's slow.
  /// </summary>
  /// <param name="mesh"></param>
  /// <param name="materialId"></param>
  /// <returns></returns>
  private List<DB.GeometryObject> RecreateMeshWithMaterial(DB.Mesh mesh, DB.ElementId materialId)
  {
    using var tsb = new DB.TessellatedShapeBuilder()
    {
      Target = DB.TessellatedShapeBuilderTarget.Mesh,
      Fallback = DB.TessellatedShapeBuilderFallback.Salvage,
      GraphicsStyleId = DB.ElementId.InvalidElementId
    };
    
    tsb.OpenConnectedFaceSet(false);
    for (int i = 0; i < mesh.NumTriangles; i++)
    {
      var triangle = mesh.get_Triangle(i);
      var points = new[]
      {
        mesh.Vertices[(int)triangle.get_Index(0)],
        mesh.Vertices[(int)triangle.get_Index(1)],
        mesh.Vertices[(int)triangle.get_Index(2)]
      };
      tsb.AddFace(new DB.TessellatedFace(points, materialId));
    }
    tsb.CloseConnectedFaceSet();
    tsb.Build();
    return tsb.GetBuildResult().GetGeometricalObjects().ToList();
  }
}
