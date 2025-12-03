using Speckle.Objects.Other;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToHost.Helpers;

/// <summary>
/// Handler for decoding raw-encoded objects (SolidX, DataObject with encoding) back to ACAD/Civil entities.
/// </summary>
public static class RawEncodingToHost
{
#pragma warning disable IDE0060 // Remove unused parameter - kept for API consistency and future use
  public static List<ADB.Entity> Convert(SOG.IRawEncodedObject target, ADB.Database targetDb)
#pragma warning restore IDE0060
  {
    if (target?.encodedValue == null)
    {
      throw new ArgumentNullException(nameof(target), "Raw encoded object or its encodedValue cannot be null.");
    }

    return Convert(target.encodedValue);
  }

  /// <summary>
  /// Converts a RawEncoding directly to AutoCAD entities.
  /// </summary>
  public static List<ADB.Entity> Convert(RawEncoding encoding)
  {
    if (encoding == null)
    {
      throw new ArgumentNullException(nameof(encoding), "RawEncoding cannot be null.");
    }

    // Route to appropriate handler based on format
    switch (encoding.format)
    {
      case RawEncodingFormats.ACAD_SAT:
        return HandleSat(encoding);
      default:
        throw new ConversionException(
          $"Unsupported raw encoding format: {encoding.format}. Expected '{RawEncodingFormats.ACAD_SAT}'."
        );
    }
  }

  /// <summary>
  /// Handles decoding of SAT (ACIS) format.
  /// </summary>
  private static List<ADB.Entity> HandleSat(RawEncoding encoding)
  {
    try
    {
      // Decode base64 to bytes
      var satBytes = System.Convert.FromBase64String(encoding.contents);

      // Create a temporary file for the SAT data
      string tempFile = System.IO.Path.GetTempFileName();
      string tempSatFile = System.IO.Path.ChangeExtension(tempFile, ".sat");

      try
      {
        // Write SAT bytes to temp file
        System.IO.File.WriteAllBytes(tempSatFile, satBytes);

        // Import SAT file using Body.AcisIn
        ADB.DBObjectCollection importedObjects = ADB.Body.AcisIn(tempSatFile);

        // Extract entities
        var entities = new List<ADB.Entity>();
        foreach (ADB.DBObject obj in importedObjects)
        {
          if (obj is ADB.Entity entity)
          {
            // Clone the entity to ensure it's detached
            var clonedEntity = (ADB.Entity)entity.Clone();
            entities.Add(clonedEntity);
          }
          // Dispose the original object from the collection
          obj.Dispose();
        }

        return entities;
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
    catch (System.Exception ex)
    {
      throw new ConversionException($"Failed to decode SAT format: {ex.Message}", ex);
    }
  }
}
