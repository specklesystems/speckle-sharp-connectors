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
  private readonly ITypedConverter<RawEncoding, List<ADB.Entity>> _rawEncodingConverter;
  private readonly EntityUnitConverter _entityUnitConverter;
  private readonly ISdkActivityFactory _activityFactory;

  public SolidXToHostConverter(
    ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> meshConverter,
    ITypedConverter<RawEncoding, List<ADB.Entity>> rawEncodingConverter,
    EntityUnitConverter entityUnitConverter,
    ISdkActivityFactory activityFactory
  )
  {
    _meshConverter = meshConverter;
    _rawEncodingConverter = rawEncodingConverter;
    _entityUnitConverter = entityUnitConverter;
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
        List<ADB.Entity> entities = _rawEncodingConverter.Convert(target.encodedValue);

        if (entities.Count > 0)
        {
          // SAT format is unitless - scale entities if source and target units differ
          _entityUnitConverter.ScaleIfNeeded(entities, target.units);
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
