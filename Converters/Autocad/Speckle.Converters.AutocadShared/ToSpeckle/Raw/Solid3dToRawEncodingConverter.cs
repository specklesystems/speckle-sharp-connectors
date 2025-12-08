using Speckle.Converters.Common.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

/// <summary>
/// Converts AutoCAD Solid3d to SAT (ACIS) raw encoding for lossless round-trip.
/// </summary>
public class Solid3dToRawEncodingConverter : ITypedConverter<ADB.Solid3d, RawEncoding>
{
  public RawEncoding Convert(ADB.Solid3d target)
  {
    if (target == null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    string tempFile = System.IO.Path.GetTempFileName();
    string tempSatFile = System.IO.Path.ChangeExtension(tempFile, ".sat");

    try
    {
      // Create collection with the solid
      using var collection = new ADB.DBObjectCollection();
      collection.Add(target);

      // Export to SAT using Body.AcisOut
      ADB.Body.AcisOut(tempSatFile, collection);

      // Read file bytes and convert to base64
      var satBytes = System.IO.File.ReadAllBytes(tempSatFile);
      var satString = System.Convert.ToBase64String(satBytes);

      return new RawEncoding { contents = satString, format = RawEncodingFormats.ACAD_SAT };
    }
    catch (System.Exception ex) when (!ex.IsFatal())
    {
      throw new ConversionException($"Failed to encode Solid3d to SAT format: {ex.Message}", ex);
    }
    finally
    {
      // Clean up temporary files
      if (System.IO.File.Exists(tempSatFile))
      {
        System.IO.File.Delete(tempSatFile);
      }
      if (System.IO.File.Exists(tempFile))
      {
        System.IO.File.Delete(tempFile);
      }
    }
  }
}
