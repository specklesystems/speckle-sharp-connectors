using Speckle.Converters.Common;
using Speckle.Converters.Common.FileOps;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class IRawEncodedObjectConverter : ITypedConverter<SOG.IRawEncodedObject, List<DB.GeometryObject>>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _settings;
  private readonly ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> _meshConverter;
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;

  public IRawEncodedObjectConverter(
    IConverterSettingsStore<RevitConversionSettings> settings,
    ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> meshConverter,
    RevitToHostCacheSingleton revitToHostCacheSingleton
  )
  {
    _settings = settings;
    _meshConverter = meshConverter;
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
      _revitToHostCacheSingleton.MaterialsByObjectId.TryGetValue(
        targetAsBase.applicationId ?? targetAsBase.id.NotNull(),
        out var mappedElementId
      )
    )
    {
      materialId = mappedElementId;
    }

    if (materialId == DB.ElementId.InvalidElementId)
    {
      return shapeImportResult.ToList(); // exit fast if there's no material id associated with this object
    }

    // check whether the results have any meshes inside - if yes, it means the shape importer produced a subpar result.
    // as we cannot paint meshes later (as you can solid faces), we need to create them now.
    // we'll default to using the display value of the original object as it's a better fallback.
    // note: if you're tempted to try and re-mesh the shape importer's meshes, don't - they are garbage.
    var hasMesh = shapeImportResult.Any(o => o is DB.Mesh);
    if (!hasMesh)
    {
      return shapeImportResult.ToList();
    }

    var displayValue = targetAsBase.TryGetDisplayValue<SOG.Mesh>().NotNull();
    var returnList = new List<DB.GeometryObject>();
    foreach (var mesh in displayValue)
    {
      mesh.applicationId = targetAsBase.applicationId ?? targetAsBase.id; // to properly map materials
      returnList.AddRange(_meshConverter.Convert(mesh));
    }

    return returnList;
  }
}
