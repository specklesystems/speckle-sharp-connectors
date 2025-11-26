using Speckle.Objects.Other;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToHost.Helpers;

/// <summary>
/// Handler for decoding raw-encoded objects (SolidX) back to ACAD/Civil entities.
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

    // Route to appropriate handler based on format
    switch (target.encodedValue.format)
    {
      case RawEncodingFormats.ACAD_DWG:
        return HandleDwg(target);
      default:
        throw new ConversionException(
          $"Unsupported raw encoding format: {target.encodedValue.format}. Expected '{RawEncodingFormats.ACAD_DWG}'."
        );
    }
  }

  /// <summary>
  /// Handles decoding of DWG binary format.
  /// </summary>
  private static List<ADB.Entity> HandleDwg(SOG.IRawEncodedObject target)
  {
    try
    {
      // Decode base64 to bytes
      var dwgBytes = System.Convert.FromBase64String(target.encodedValue.contents);

      // Create a temporary file for the DWG data
      // (AutoCAD API requires a file path for reading DWG databases)
      string tempFile = System.IO.Path.GetTempFileName();
      string tempDwgFile = System.IO.Path.ChangeExtension(tempFile, ".dwg");

      try
      {
        // Write DWG bytes to temp file
        System.IO.File.WriteAllBytes(tempDwgFile, dwgBytes);

        // Read the DWG database
        using var sourceDb = new ADB.Database(false, true);
        sourceDb.ReadDwgFile(tempDwgFile, System.IO.FileShare.Read, true, null);

        // Extract entities from ModelSpace
        var entities = new List<ADB.Entity>();

        using var tr = sourceDb.TransactionManager.StartTransaction();
        var bt = (ADB.BlockTable)tr.GetObject(sourceDb.BlockTableId, ADB.OpenMode.ForRead);
        var btr = (ADB.BlockTableRecord)tr.GetObject(bt[ADB.BlockTableRecord.ModelSpace], ADB.OpenMode.ForRead);

        foreach (ADB.ObjectId objId in btr)
        {
          if (tr.GetObject(objId, ADB.OpenMode.ForRead) is ADB.Entity entity)
          {
            // Clone the entity to ensure it's not tied to the source database
            var clonedEntity = (ADB.Entity)entity.Clone();
            entities.Add(clonedEntity);
          }
        }

        tr.Commit();

        return entities;
      }
      finally
      {
        // Clean up temporary files
        try
        {
          if (System.IO.File.Exists(tempDwgFile))
          {
            System.IO.File.Delete(tempDwgFile);
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
      throw new ConversionException($"Failed to decode DWG format: {ex.Message}", ex);
    }
  }
}
