using Speckle.Connector.Navisworks.Services;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connector.Navisworks.Operations.Send.Filters;

public class NavisworksSavedSetsFilter : DiscriminatedObject, ISendFilterSelect
{
  private readonly IElementSelectionService _selectionService;

  public NavisworksSavedSetsFilter(IElementSelectionService selectionService)
  {
    _selectionService = selectionService;

    GetSavedSets();
  }

  public string Id { get; set; } = "navisworksSavedSets";
  public string Name { get; set; } = "Saved Sets";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public List<string> SelectedObjectIds { get; set; } = [];
  public Dictionary<string, string>? IdMap { get; set; }

  public bool IsMultiSelectable { get; set; } = true;
  public List<SendFilterSelectItem> SelectedItems { get; set; }
  public List<SendFilterSelectItem> Items { get; set; }

  public List<string> RefreshObjectIds()
  {
    List<string> objectIds = [];

    if (SelectedItems.Count == 0)
    {
      return objectIds;
    }

    NAV.SavedItemCollection? selectionSets = NavisworksApp.ActiveDocument.SelectionSets.RootItem.Children;

    foreach (var selectedSetGuid in SelectedItems)
    {
      var guid = new Guid(selectedSetGuid.Id);
      var index = selectionSets.IndexOfGuid(guid);

      if (index == -1)
      {
        throw new SpeckleSendFilterException($"Selection set with GUID {guid} not found.");
      }

      var selectionSetItem = selectionSets[index];
      var selectionSet = (NAV.SelectionSet)selectionSetItem;

      if (selectionSet.HasSearch)
      {
        objectIds.AddRange(ResolveSearchSet(selectionSet.Search));
      }

      if (selectionSet.HasExplicitModelItems)
      {
        objectIds.AddRange(ResolveSelectionSet(selectionSet.ExplicitModelItems));
      }
    }

    return objectIds;
  }

  private IEnumerable<string> ResolveSelectionSet(NAV.ModelItemCollection selectionSetExplicitModelItems) =>
    selectionSetExplicitModelItems
      .Where(_selectionService.IsVisible) // Exclude hidden elements
      .Select(_selectionService.GetModelItemPath) // Resolve to index paths
      .ToList();

  private IEnumerable<string> ResolveSearchSet(NAV.Search selectionSetSearch) =>
    selectionSetSearch
      .FindAll(NavisworksApp.ActiveDocument, false)
      .Where(_selectionService.IsVisible) // Exclude hidden elements
      .Select(_selectionService.GetModelItemPath) // Resolve to index paths
      .ToList();

  /// <summary>
  /// Since it is called from constructor, it is re-called whenever UI calls SendBinding.GetSendFilters() on SendFilter dialog.
  /// Do not change the behavior/scope of this class on send binding unless make sure the behavior is same. Otherwise we might not be able to update list of saved sets.
  /// </summary>
  private void GetSavedSets()
  {
    List<NAV.SavedItem> savedSetRecords = NavisworksApp
      .ActiveDocument.SelectionSets.RootItem.Children.Where(set => !set.IsGroup)
      .ToList();

    Items = savedSetRecords
      .Select(setRecord =>
      {
        NAV.SavedItem? record = setRecord.CreateCopy();
        string? name = record.DisplayName;

        while (record.Parent != null)
        {
          name = record.Parent.DisplayName + "::" + name;
          record = record.Parent;
        }

        return new SendFilterSelectItem(setRecord.Guid.ToString(), name);
      })
      .ToList();
  }
}
