using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Generic converter for Plant3D entity types.
/// Plant3D entities are internally block references (often nested).
/// Uses Explode() to decompose into world-coordinate geometry, then converts each
/// sub-entity using the existing AutoCAD converters (Line, Arc, Solid3d, etc.).
/// </summary>
public abstract class Plant3dEntityToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _converterManager;
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;

  private const int MAX_DEPTH = 5;

  protected Plant3dEntityToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> converterManager,
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore
  )
  {
    _converterManager = converterManager;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => ConvertEntity((ADB.Entity)target);

  private DataObject ConvertEntity(ADB.Entity entity)
  {
    List<Base> displayValue = ExtractDisplayValue(entity);

    DataObject dataObject =
      new()
      {
        name = entity.GetType().Name,
        displayValue = displayValue,
        properties = new Dictionary<string, object?>(),
        applicationId = entity.Handle.Value.ToString()
      };

    return dataObject;
  }

#pragma warning disable CA1031 // Autodesk APIs throw various exception types
  private List<Base> ExtractDisplayValue(ADB.Entity entity)
  {
    List<Base> results = new();
    CollectDisplayObjects(entity, results, 0);
    return results;
  }

  /// <summary>
  /// Recursively explodes the entity to reach leaf geometry, then converts
  /// each piece using the registered AutoCAD converters.
  /// Explode() produces entities in world coordinates (transforms are applied),
  /// unlike opening the BlockTableRecord directly which gives local coordinates.
  /// </summary>
  private void CollectDisplayObjects(ADB.Entity entity, List<Base> results, int depth)
  {
    // If this is NOT a block reference, try converting it directly
    // (Line, Arc, Circle, Polyline, Solid3d, etc.)
    if (entity is not ADB.BlockReference)
    {
      try
      {
        var converter = _converterManager.ResolveConverter(entity.GetType(), false);
        if (converter is not null)
        {
          var converted = converter.Convert(entity);
          results.Add(converted);
          return;
        }
      }
      catch (System.Exception)
      {
        // Converter not found or failed — fall through to explode
      }
    }

    // For BlockReferences or unconvertible entities, explode to get sub-entities
    // Explode produces world-coordinate geometry (block transform is applied)
    if (depth >= MAX_DEPTH)
    {
      return;
    }

    try
    {
      using ADB.DBObjectCollection exploded = new();
      entity.Explode(exploded);
      foreach (ADB.DBObject obj in exploded)
      {
        if (obj is ADB.Entity subEntity)
        {
          CollectDisplayObjects(subEntity, results, depth + 1);
        }
      }
    }
    catch (System.Exception)
    {
      // Can't explode — no display value from this entity
    }
  }
#pragma warning restore CA1031
}
