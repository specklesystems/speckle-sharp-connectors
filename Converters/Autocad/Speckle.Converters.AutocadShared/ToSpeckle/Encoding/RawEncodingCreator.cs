using Speckle.Objects.Other;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToSpeckle.Encoding;

/// <summary>
/// Creates raw encoded representations of AutoCAD geometry using DWG binary format.
/// </summary>
internal static class RawEncodingCreator
{
  public static RawEncoding Encode(ADB.Solid3d target, ADB.Database sourceDb)
  {
    ArgumentNullException.ThrowIfNull(target);

    ArgumentNullException.ThrowIfNull(sourceDb);

    string tempFile = System.IO.Path.GetTempFileName();
    string tempDwgFile = System.IO.Path.ChangeExtension(tempFile, ".dwg");

    try
    {
      // Create a new in-memory database
      using var tempDb = new ADB.Database(true, false);

      // Copy unit settings from source database to preserve scale
      tempDb.Insunits = sourceDb.Insunits;

      // Open ModelSpace for write
      using var tr = tempDb.TransactionManager.StartTransaction();
      var bt = (ADB.BlockTable)tr.GetObject(tempDb.BlockTableId, ADB.OpenMode.ForRead);
      var btr = (ADB.BlockTableRecord)tr.GetObject(bt[ADB.BlockTableRecord.ModelSpace], ADB.OpenMode.ForWrite);

      // Clone the solid to the new database
      var idMapping = new ADB.IdMapping();
      var objectIds = new ADB.ObjectIdCollection { target.ObjectId };
      sourceDb.WblockCloneObjects(objectIds, btr.ObjectId, idMapping, ADB.DuplicateRecordCloning.Replace, false);

      tr.Commit();

      // Save database to temp file as DWG
      tempDb.SaveAs(tempDwgFile, ADB.DwgVersion.Current);

      // Read file bytes and convert to base64
      var dwgBytes = System.IO.File.ReadAllBytes(tempDwgFile);
      var dwgString = System.Convert.ToBase64String(dwgBytes);

      return new RawEncoding { contents = dwgString, format = RawEncodingFormats.ACAD_DWG };
    }
    catch (System.Exception ex)
    {
      throw new ConversionException($"Failed to encode Solid3d to DWG format: {ex.Message}", ex);
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
}
