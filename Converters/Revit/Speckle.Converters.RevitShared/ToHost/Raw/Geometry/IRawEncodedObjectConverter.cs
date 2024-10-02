using Speckle.Converters.Common;
using Speckle.Converters.Common.FileOps;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;

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
    var raw = target.encodedValue.contents;
    var bytes = System.Convert.FromBase64String(raw!);
    var filePath = TempFileProvider.GetTempFile("RevitX", target.encodedValue.format);
    File.WriteAllBytes(filePath, bytes);

    using var importer = new DB.ShapeImporter();
    var shapeImportResult = importer.Convert(_settings.Current.Document, filePath);

    // Old but gold Note: we might want to export in the future single breps from rhino as multiple ones to bypass limitations of the geometry kernel here; tbd - but we should not necessarily assume a single shape

    return shapeImportResult.ToList();
  }
}
