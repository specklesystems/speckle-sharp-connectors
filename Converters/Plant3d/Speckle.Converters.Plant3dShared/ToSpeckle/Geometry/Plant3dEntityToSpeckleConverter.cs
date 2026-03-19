using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Generic converter for Plant3D entity types.
/// Extracts display mesh via Explode (Plant3D entities are typically block references)
/// and creates a DataObject with Plant3D properties.
/// </summary>
public abstract class Plant3dEntityToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;

  protected Plant3dEntityToSpeckleConverter(
    ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter,
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore
  )
  {
    _brepConverter = brepConverter;
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

  /// <summary>
  /// Extracts display meshes from a Plant3D entity.
  /// Plant3D entities are typically block references, so we explode them
  /// and extract meshes from the constituent Solid3d entities.
  /// </summary>
#pragma warning disable CA1031 // Autodesk APIs throw various exception types during BREP/Explode
  private List<Base> ExtractDisplayValue(ADB.Entity entity)
  {
    List<Base> meshes = new();

    // First try direct BREP extraction
    try
    {
      using ABR.Brep brep = new(entity);
      if (!brep.IsNull)
      {
        meshes.Add(_brepConverter.Convert(brep));
        return meshes;
      }
    }
    catch (System.Exception)
    {
      // Expected for block references — fall through to explode
    }

    // Plant3D entities are typically block references — explode to get geometry
    try
    {
      using ADB.DBObjectCollection exploded = new();
      entity.Explode(exploded);
      foreach (ADB.DBObject obj in exploded)
      {
        if (obj is ADB.Solid3d solid)
        {
          try
          {
            using ABR.Brep brep = new(solid);
            if (!brep.IsNull)
            {
              meshes.Add(_brepConverter.Convert(brep));
            }
          }
          catch (System.Exception)
          {
            // Skip individual solids that fail
          }
        }
        else if (obj is ADB.Entity subEntity)
        {
          try
          {
            using ABR.Brep brep = new(subEntity);
            if (!brep.IsNull)
            {
              meshes.Add(_brepConverter.Convert(brep));
            }
          }
          catch (System.Exception)
          {
            // Skip sub-entities that can't be meshed
          }
        }
      }
    }
    catch (System.Exception)
    {
      // Entity can't be exploded
    }

    return meshes;
  }
#pragma warning restore CA1031
}
