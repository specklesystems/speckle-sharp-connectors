using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToSpeckle.Encoding;

internal static class RawEncodingCreator
{
  public static SO.RawEncoding Encode(RG.GeometryBase target, RhinoDoc doc)
  {
    // note: this way works too, but we need to write the file to disk before reading it back out.
    // using var doc = RhinoDoc.CreateHeadless(default);
    // doc.ModelUnitSystem = _settingsStore.Current.Document.ModelUnitSystem;
    // doc.ModelAbsoluteTolerance = _settingsStore.Current.Document.ModelAbsoluteTolerance;
    // doc.ModelAngleToleranceRadians = _settingsStore.Current.Document.ModelAngleToleranceRadians;
    // doc.Objects.Add(target);

    // var tempFile = TempFileProvider.GetTempFile(_speckleApplication.Slug, "3dm");
    // doc.Write3dmFile(tempFile, new FileWriteOptions() { IncludeRenderMeshes = false, WriteGeometryOnly = true, IncludeHistory = false, WriteUserData = false});
    // var fileBytes = System.Convert.ToBase64String(File.ReadAllBytes(tempFile));
    // var brepXEncoding = new SOG.BrepXEncoding() { contents = fileBytes, format = "3dm" };
    // return brepXEncoding;

    // note: this way works probably better as we don't need to write the file to disk and read it back in.
    using var file = new File3dm();
    switch (target)
    {
      case RG.Brep b:
        file.Objects.AddBrep(b);
        break;
      case RG.Extrusion e:
        file.Objects.AddExtrusion(e);
        break;
      case RG.SubD d:
        file.Objects.AddSubD(d);
        break;
      default:
        throw new ConversionException($"Unsupported type for encoding: {target.GetType().FullName}");
    }

    file.Settings.ModelUnitSystem = doc.ModelUnitSystem;
    file.Settings.ModelAbsoluteTolerance = doc.ModelAbsoluteTolerance;
    file.Settings.ModelAngleToleranceRadians = doc.ModelAngleToleranceRadians;

    file.AllLayers.Clear();
    file.AllMaterials.Clear();
    file.Views.Clear();
    file.NamedViews.Clear();
    file.AllDimStyles.Clear();
    file.AllGroups.Clear();
    file.AllHatchPatterns.Clear();

    File3dmWriteOptions options = new() { SaveUserData = false, Version = 7 };
    options.EnableRenderMeshes(ObjectType.Brep, false);
    options.EnableRenderMeshes(ObjectType.Extrusion, false);
    var fb = file.ToByteArray(options);
    var fbString = Convert.ToBase64String(fb);
    var bxe = new SO.RawEncoding() { contents = fbString, format = SO.RawEncodingFormats.RHINO_3DM };
    return bxe;
  }
}
