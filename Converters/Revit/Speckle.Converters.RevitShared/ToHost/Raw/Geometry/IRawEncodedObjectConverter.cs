using Speckle.Converters.Common;
using Speckle.Converters.Common.FileOps;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.Revit2023.ToHost.Raw.Geometry;

public class IRawEncodedObjectConverter : ITypedConverter<SOG.IRawEncodedObject, List<DB.GeometryObject>>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _settings;

  public IRawEncodedObjectConverter(IConverterSettingsStore<RevitConversionSettings> settings)
  {
    _settings = settings;
  }
  
  public List<DB.GeometryObject> Convert(SOG.IRawEncodedObject target)
  {
    var raw = target.encodedValue.contents;
    var bytes = System.Convert.FromBase64String(raw!);
    var filePath = TempFileProvider.GetTempFile("RevitX", target.encodedValue.format);
    File.WriteAllBytes(filePath, bytes);

    using var importer = new DB.ShapeImporter();
    var shapeImportResult = importer.Convert(_settings.Current.Document, filePath).OfType<DB.GeometryObject>();
    
    //   // note: scaling is a todo
    //   // DB.SolidUtils.CreateTransformed(shape, DB.Transform.Identity);
    
    // _settings.Document.Paint();
    
    return shapeImportResult.ToList();
  }
}
