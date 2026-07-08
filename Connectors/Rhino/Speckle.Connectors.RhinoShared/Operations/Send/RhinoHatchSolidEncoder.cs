using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Speckle.Objects.Other;
using RG = Rhino.Geometry;

namespace Speckle.Connectors.Rhino.Operations.Send;

internal static class RhinoHatchSolidEncoder
{
  public static RawEncoding? TryEncode(RhinoObject rhinoObject, RhinoDoc doc)
  {
    if (rhinoObject.Geometry is not RG.Hatch hatch)
    {
      return null;
    }
    using var file = new File3dm();
    file.Objects.AddHatch(hatch);
    file.Settings.ModelUnitSystem = doc.ModelUnitSystem;
    file.Settings.ModelAbsoluteTolerance = doc.ModelAbsoluteTolerance;
    file.Settings.ModelAngleToleranceRadians = doc.ModelAngleToleranceRadians;
    var bytes = file.ToByteArray(new File3dmWriteOptions() { SaveUserData = false, Version = 7 });
    return new RawEncoding() { contents = System.Convert.ToBase64String(bytes), format = RawEncodingFormats.RHINO_3DM };
  }
}
