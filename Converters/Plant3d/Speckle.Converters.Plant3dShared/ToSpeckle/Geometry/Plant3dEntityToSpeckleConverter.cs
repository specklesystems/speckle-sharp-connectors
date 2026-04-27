using Speckle.Converters.Autocad.Extensions;
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

  private const int MAX_DEPTH = 5;

  protected Plant3dEntityToSpeckleConverter(IConverterManager<IToSpeckleTopLevelConverter> converterManager)
  {
    _converterManager = converterManager;
  }

  public Base Convert(object target) => ConvertEntity((ADB.Entity)target);

  private DataObject ConvertEntity(ADB.Entity entity)
  {
    List<Base> displayValue = ExtractDisplayValue(entity);

    // Plant3D P&ID types (Asset, LineSegment, EndLineObject, etc.) each have a TagValue property
    // that provides a unique, human-readable identifier (e.g. "T-0001", "1 1/2"-CIPS-Util1049-...").
    // These types all inherit directly from ADB.Entity — there is no shared P&ID base class
    // that declares TagValue, so we use reflection to read it regardless of the concrete type.
    // I don't like this but its a good tradeoff since Plant3d API does not have common class to find TagValue
    string name = entity.GetType().Name;
    if (entity.GetType().GetProperty("TagValue")?.GetValue(entity) is string tagValue && tagValue.Length > 0)
    {
      name = tagValue;
    }

    DataObject dataObject = new()
    {
      name = name,
      displayValue = displayValue,
      properties = new Dictionary<string, object?>(),
      applicationId = entity.Handle.Value.ToString(),
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
    // ATTDEFs in a block definition hold the field template (e.g. "#(TargetObject.Type)"),
    // not the rendered text. The real string lives on each instance's AttributeReference,
    // which we capture below from the parent BlockReference's AttributeCollection.
    if (entity is ADB.AttributeDefinition)
      return;
    }

    // If this is NOT a block reference, try converting it directly
    // (Line, Arc, Circle, Polyline, Solid3d, etc.)
    if (entity is not ADB.BlockReference blockRef)
    {
      try
      {
        var converter = _converterManager.ResolveConverter(entity.GetType(), false);

        var converted = converter.Convert(entity);
        results.Add(converted);
        return;
      }
      catch (System.Exception)
      {
        // Fall through to explode on ConversionNotSupportedException or failed
      }
    }
    else
    {
      // AttributeReference inherits from DBText, so it resolves
      // the existing DBText converter without any template parsing on our side.
      foreach (
        ADB.AttributeReference attRef in blockRef.GetSubEntities<ADB.AttributeReference>(
          source: blockRef.AttributeCollection
        )
      )
      {
        if (!attRef.Visible || string.IsNullOrWhiteSpace(attRef.TextString))
        {
          continue;
        }

        try
        {
          var converter = _converterManager.ResolveConverter(attRef.GetType());
          results.Add(converter.Convert(attRef));
        }
        catch (System.Exception)
        {
          // Skip any attribute reference we can't convert; continue with the rest.
        }
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
