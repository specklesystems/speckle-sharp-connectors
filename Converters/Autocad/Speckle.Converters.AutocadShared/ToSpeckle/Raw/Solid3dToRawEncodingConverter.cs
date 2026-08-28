using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

/// <summary>
/// Converts AutoCAD Solid3d to SAT (ACIS) raw encoding for lossless round-trip.
/// </summary>
public class Solid3dToRawEncodingConverter(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  : ITypedConverter<ADB.Solid3d, RawEncoding>
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
      // SAT stores the solid's own coordinates and bypasses the point/vector converters. Apply the selected
      // placement to a disposable clone so baked sends keep the authoritative SAT solid aligned with its display
      // mesh. Definition-member conversions suppress ReferencePointTransform in the settings store, so their SAT
      // remains definition-local.
      using var collection = new ADB.DBObjectCollection();
      using ADB.Solid3d? transformed = settingsStore.Current.ReferencePointTransform is not null
        ? (ADB.Solid3d)target.Clone()
        : null;
      if (settingsStore.Current.ReferencePointTransform is AG.Matrix3d sourceToWcs)
      {
        transformed!.TransformBy(sourceToWcs.Inverse());
      }
      collection.Add(transformed ?? target);

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
