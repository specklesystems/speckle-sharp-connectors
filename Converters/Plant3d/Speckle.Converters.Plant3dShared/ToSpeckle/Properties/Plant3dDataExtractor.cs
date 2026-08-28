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
  private readonly ILogger<Plant3dDataExtractor> _logger;

  public Plant3dDataExtractor(
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore,
    ILogger<Plant3dDataExtractor> logger
  )
  {
    _settingsStore = settingsStore;
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
      int rowId = dlm.FindAcPpRowId(entity.ObjectId);
      if (rowId <= 0)
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
}
