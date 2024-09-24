using System.Collections;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class BaseToHostGeometryObjectConverter : ITypedConverter<Base, List<DB.GeometryObject>>
{
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointConverter;
  private readonly ITypedConverter<ICurve, DB.CurveArray> _curveConverter;
  private readonly ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> _meshConverter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _settings;

  public BaseToHostGeometryObjectConverter(
    ITypedConverter<SOG.Point, DB.XYZ> pointConverter,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter,
    ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> meshConverter,
    IConverterSettingsStore<RevitConversionSettings> settings
  )
  {
    _pointConverter = pointConverter;
    _curveConverter = curveConverter;
    _meshConverter = meshConverter;
    _settings = settings;
  }

  public List<DB.GeometryObject> Convert(Base target)
  {
    List<DB.GeometryObject> result = new();

    switch (target)
    {
      case SOG.Point point:
        var xyz = _pointConverter.Convert(point);
        result.Add(DB.Point.Create(xyz));
        break;
      case ICurve curve:
        var curves = _curveConverter.Convert(curve).Cast<DB.GeometryObject>();
        result.AddRange(curves);
        break;
      case SOG.Mesh mesh:
        var meshes = _meshConverter.Convert(mesh).Cast<DB.GeometryObject>();
        result.AddRange(meshes);
        break;
      case SOG.Brep burp:
        // should be try caught and default back to mesh
        var boss = TryImportBrepShape(burp);
        if (boss != null)
        {
          result.AddRange(boss);
        }
        break;
      default:
        var displayValue = target.TryGetDisplayValue<Base>();
        if ((displayValue is IList && !displayValue.Any()) || displayValue is null)
        {
          throw new NotSupportedException($"No display value found for {target.speckle_type}");
        }

        foreach (var display in displayValue)
        {
          result.AddRange(Convert(display));
        }

        break;
    }

    return result;
  }

  public IEnumerable<DB.GeometryObject> TryImportBrepShape(SOG.Brep burp)
  {
    var burpRhinoContents = burp["fileBlob"] as string; // note: temp, this is for now 3dm specific (?)
    var fileBytes = System.Convert.FromBase64String(burpRhinoContents!);
    var filePath = Path.Combine(Path.GetTempPath(), "Speckle", "Revit Import", $"{Guid.NewGuid():N}.3dm");
    Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Speckle", "Revit Import"));
    File.WriteAllBytes(filePath, fileBytes);

    using var importer = new DB.ShapeImporter();
    var list = importer.Convert(_settings.Current.Document, filePath).OfType<DB.GeometryObject>();

    return list;
    // Note: we might want to export in the future single breps from rhino as multiple ones to bypass limitations of the geometry kernel here; tbd - but we should not necessarily assume a single shape
    // if (list.OfType<DB.Solid>().FirstOrDefault() is DB.GeometryObject shape)
    // {
    //   // note: scaling is a todo
    //   // DB.SolidUtils.CreateTransformed(shape, DB.Transform.Identity);
    //   // _settings.Document.Paint(); // note: we can pain faces post creation with whatever material we want, to make 'em look as needed
    //   return shape;
    // }
    // return null;
  }
}
