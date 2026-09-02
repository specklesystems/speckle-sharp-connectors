using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.MicroStation.Plugin;

namespace Speckle.Connectors.MicroStation.Operations.Send.Filters;

/// <summary>
/// Sends elements that belong to specific DGN levels (layers).
/// The UI populates <see cref="SelectedLevelNames"/> from <see cref="GetAvailableLevelNames"/>.
/// </summary>
public class MicroStationLevelFilter : DiscriminatedObject, ISendFilter
{
  public string Id { get; set; } = "byLevel";
  public string Type { get; set; } = "Level";
  public string Name { get; set; } = "By Level";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public List<string> SelectedObjectIds { get; set; } = [];
  public Dictionary<string, string>? IdMap { get; set; }

  /// <summary>Level names chosen by the user in the DUI3 panel.</summary>
  public List<string> SelectedLevelNames { get; set; } = [];

  public List<string> RefreshObjectIds()
  {
    var model = MsApp.ActiveModel;
    if (model == null || SelectedLevelNames.Count == 0)
    {
      return [];
    }

    // Build a set of level names to include
    var selectedNames = new HashSet<string>(SelectedLevelNames, StringComparer.OrdinalIgnoreCase);

    var ids = new List<string>();
    var enumerator = model.GraphicalElementCache.Scan(new MSIDGN.ElementScanCriteriaClass());
    while (enumerator.MoveNext())
    {
      var element = enumerator.Current;
      if (element != null && selectedNames.Contains(element.Level?.Name ?? string.Empty))
      {
        ids.Add(element.ID.ToString());
      }
    }

    return ids;
  }

  /// <summary>
  /// Returns the names of all levels available in the active DGN model.
  /// Called by the DUI3 panel to populate the level picker.
  /// </summary>
  public List<string> GetAvailableLevelNames()
  {
    var model = MsApp.ActiveModel;
    if (model == null)
    {
      return [];
    }

    var names = new List<string>();
    foreach (MSIDGN.Level level in model.Levels)
    {
      if (!string.IsNullOrEmpty(level.Name))
      {
        names.Add(level.Name);
      }
    }

    return names;
  }
}
