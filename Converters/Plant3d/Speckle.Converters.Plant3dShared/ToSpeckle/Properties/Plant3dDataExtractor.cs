using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Sdk;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Extracts Plant3D project database properties for entities.
/// Uses the PnPDataLinks API to find the data row linked to a DWG entity,
/// then reads all properties from that row in the project database (DCF files).
/// </summary>
public class Plant3dDataExtractor
{
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;
  private readonly Plant3dClassHierarchyResolver _classHierarchyResolver;
  private readonly Plant3dLineGroupResolver _lineGroupResolver;
  private readonly ILogger<Plant3dDataExtractor> _logger;

  public Plant3dDataExtractor(
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore,
    Plant3dClassHierarchyResolver classHierarchyResolver,
    Plant3dLineGroupResolver lineGroupResolver,
    ILogger<Plant3dDataExtractor> logger
  )
  {
    _settingsStore = settingsStore;
    _classHierarchyResolver = classHierarchyResolver;
    _lineGroupResolver = lineGroupResolver;
    _logger = logger;
  }

  /// <summary>
  /// Gets Plant3D database properties for the given entity.
  /// Returns a dictionary of property name → value pairs from the project database.
  /// </summary>
#pragma warning disable CA1031 // Plant3D data APIs can throw various exceptions
  public Dictionary<string, object?> GetDataProperties(ADB.Entity entity)
  {
    var result = new Dictionary<string, object?>();

    try
    {
      var database = _settingsStore.Current.Document.Database;

      // Get the DataLinksManager for this database
      var dlm = PPDL.DataLinksManager.GetManager(database);
      if (dlm is null)
      {
        return result;
      }

      // Find the data row linked to this entity (returns single int row ID)
      if (!TryGetRowId(dlm, entity, out var rowId))
      {
        return result;
      }

      // Read all properties from the linked row
      var allProps = dlm.GetAllProperties(rowId, true);
      if (allProps is not null)
      {
        foreach (var kvp in allProps)
        {
          string key = kvp.Key ?? "unknown";

          result[key] = kvp.Value;
        }
      }

      AddDataManagerProperties(dlm, entity, result);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // DataLinksManager unavailable, entity unlinked/orphaned from the PnP project database,
      // or any other DataLinks API failure — degrade to no P&ID properties instead of failing the object.
      _logger.LogWarning(ex, "Failed to read PnP DataLinks properties on object {HandleValue}", entity.Handle.Value);
    }

    return result;
  }
#pragma warning restore CA1031

  private static bool TryGetRowId(PPDL.DataLinksManager dataLinksManager, ADB.Entity entity, out int rowId)
  {
    // Checking HasLinks avoids many exceptions where the entity is not Plant object e.g. lines, text, block references
    rowId = dataLinksManager.HasLinks(entity.ObjectId) ? dataLinksManager.FindAcPpRowId(entity.ObjectId) : 0;
    return rowId > 0;
  }

  private void AddDataManagerProperties(
    PPDL.DataLinksManager dataLinksManager,
    ADB.Entity entity,
    Dictionary<string, object?> result
  )
  {
    var classHierarchy = _classHierarchyResolver.Resolve(dataLinksManager, entity.ObjectId);
    var properties = new Dictionary<string, object>()
    {
      { "Category", classHierarchy.ClassName },
      { "Level1", classHierarchy.Level1 },
      { "Level2", classHierarchy.Level2 },
      { "Level3", classHierarchy.Level3 },
      { "Level4", classHierarchy.Level4 },
      { "Level5", classHierarchy.Level5 },
    };
    result["Data Manager"] = properties;

    if (_lineGroupResolver.TryGetGroupId(dataLinksManager, entity.ObjectId, out var groupId))
    {
      properties["GroupId"] = groupId;
    }
  }
}
