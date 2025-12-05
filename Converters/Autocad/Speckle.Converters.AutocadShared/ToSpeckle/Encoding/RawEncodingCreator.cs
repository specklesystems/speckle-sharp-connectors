using Speckle.Objects.Other;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToSpeckle.Encoding;

/// <summary>
/// Creates raw encoded representations of AutoCAD geometry using SAT format.
/// </summary>
internal static class RawEncodingCreator
{
  /// <summary>
  /// Encodes an AutoCAD Solid3d to SAT (ACIS) format.
  /// SAT format is smaller than DWG as it only contains geometry data.
  /// </summary>
  public static RawEncoding Encode(ADB.Solid3d target)
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
    catch (System.Exception ex)
    {
      throw new ConversionException($"Failed to encode Solid3d to SAT format: {ex.Message}", ex);
    }
    finally
    {
      // Clean up temporary files
      try
      {
        if (System.IO.File.Exists(tempSatFile))
        {
          System.IO.File.Delete(tempSatFile);
        }
        if (System.IO.File.Exists(tempFile))
        {
          System.IO.File.Delete(tempFile);
        }
      }
#pragma warning disable CA1031 // Catching general exception for cleanup - failures are intentionally ignored
      catch
      {
        // Ignore cleanup errors
      }
#pragma warning restore CA1031
    }
  }
}
