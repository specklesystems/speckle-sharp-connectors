using Speckle.Converters.Autocad.ToHost.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

/// <summary>
/// Converts a SolidX to AutoCAD Solid3d entities with lossless round-tripping via SAT encoding.
/// </summary>
[NameAndRankValue(typeof(SOG.SolidX), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SolidXToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.SolidX, List<(ADB.Entity a, Base b)>>
{
  private readonly ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> _meshConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly ISdkActivityFactory _activityFactory;

  public SolidXToHostConverter(
    ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> meshConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    ISdkActivityFactory activityFactory
  )
  {
    _meshConverter = meshConverter;
    _settingsStore = settingsStore;
    _activityFactory = activityFactory;
  }

  public object Convert(Base target) => Convert((SOG.SolidX)target);

  public List<(ADB.Entity a, Base b)> Convert(SOG.SolidX target)
  {
    // Try to decode raw encoding first for lossless conversion
    if (target.encodedValue?.format == RawEncodingFormats.ACAD_SAT)
    {
      try
      {
        var database = _settingsStore.Current.Document.Database;
        List<ADB.Entity> entities = RawEncodingToHost.Convert(target, database);

        if (entities.Count > 0)
        {
          // Successfully decoded - return the native entities
          // Map all entities to the same source SolidX for tracking
          return entities.Select(entity => ((ADB.Entity)entity, (Base)target)).ToList();
        }
      }
      catch (ConversionException ex)
      {
        // Log the failure and fall through to displayValue fallback
        using var activity = _activityFactory.Start("SolidX Raw Encoding Fallback");
        activity?.SetStatus(SdkActivityStatusCode.Error);
        activity?.RecordException(ex);
      }
    }

    // Fallback: Convert displayValue meshes to PolyFaceMesh
    var result = new List<ADB.PolyFaceMesh>();
    foreach (SOG.Mesh mesh in target.displayValue)
    {
      ADB.PolyFaceMesh convertedMesh = _meshConverter.Convert(mesh);
      result.Add(convertedMesh);
    }

    return result.Zip(target.displayValue, (a, b) => ((ADB.Entity)a, (Base)b)).ToList();
  }
}
