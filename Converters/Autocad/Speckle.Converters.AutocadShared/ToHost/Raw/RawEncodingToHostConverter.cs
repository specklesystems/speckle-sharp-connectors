using Speckle.Converters.Common.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToHost.Raw;

/// <summary>
/// Converts RawEncoding (SAT format) to AutoCAD entities.
/// </summary>
public class RawEncodingToHostConverter : ITypedConverter<RawEncoding, List<ADB.Entity>>
{
  public List<ADB.Entity> Convert(RawEncoding target)
  {
    if (target == null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    // Route to appropriate handler based on format
    switch (target.format)
    {
      case RawEncodingFormats.ACAD_SAT:
        return HandleSat(target);
      default:
        throw new ConversionException(
          $"Unsupported raw encoding format: {target.format}. Expected '{RawEncodingFormats.ACAD_SAT}'."
        );
    }
  }

  /// <summary>
  /// Handles decoding of SAT (ACIS) format.
  /// </summary>
  private List<ADB.Entity> HandleSat(RawEncoding encoding)
  {
    try
    {
      var satBytes = System.Convert.FromBase64String(encoding.contents);

      // Create a temporary file for the SAT data
      string tempFile = System.IO.Path.GetTempFileName();
      string tempSatFile = System.IO.Path.ChangeExtension(tempFile, ".sat");

      try
      {
        System.IO.File.WriteAllBytes(tempSatFile, satBytes);

        ADB.DBObjectCollection importedObjects = ADB.Body.AcisIn(tempSatFile);

        var entities = new List<ADB.Entity>();
        foreach (ADB.DBObject obj in importedObjects)
        {
          if (obj is ADB.Entity entity)
          {
            // Clone the entity to ensure it's detached
            var clonedEntity = (ADB.Entity)entity.Clone();
            entities.Add(clonedEntity);
          }
          obj.Dispose();
        }

        return entities;
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
    catch (System.Exception ex) when (!ex.IsFatal())
    {
      throw new ConversionException($"Failed to decode SAT format: {ex.Message}", ex);
    }
  }
}
