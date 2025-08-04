using Speckle.Connector.Navisworks.Services;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connector.Navisworks.Operations.Send.Filters;

public class NavisworksSavedViewsFilter : DiscriminatedObject, ISendFilterSelect
{
  private readonly IElementSelectionService _selectionService;

  public NavisworksSavedViewsFilter(IElementSelectionService selectionService)
  {
    _selectionService = selectionService;

    Items = [];
    SelectedItems = [];

    GetSavedViews();
  }

  public string Id { get; set; } = "navisworksSavedViews";

  public string Name { get; set; } = "Saved Views";

  public string Type { get; set; } = "Select";

  public string? Summary { get; set; }

  public bool IsDefault { get; set; }

  public List<string> SelectedObjectIds { get; set; } = [];

  public Dictionary<string, string>? IdMap { get; set; }

  public bool IsMultiSelectable { get; set; }

  public List<SendFilterSelectItem> SelectedItems { get; set; }

  public List<SendFilterSelectItem> Items { get; set; }

  public List<string> RefreshObjectIds()
  {
    List<string> objectIds = [];

    if (SelectedItems.Count == 0)
    {
      return objectIds;
    }

    var savedViews = NavisworksApp.ActiveDocument.SavedViewpoints;

    foreach (var savedViewItem in SelectedItems.Select(item => ResolveSavedView(item.Id)))
    {
      // Get the visible elements in the saved view.
      objectIds.AddRange(ResolvedSavedViewObjects(savedViewItem));
    }

    return objectIds;
  }

  private static NAV.SavedViewpoint ResolveSavedView(string savedViewReference)
  {
    if (Guid.TryParse(savedViewReference, out var guid))
    {
      // Even though we may have already got a match, that could be to a generic Guid from earlier versions of Navisworks
      if (savedViewReference != Guid.Empty.ToString())
      {
        return (NAV.SavedViewpoint)NavisworksApp.ActiveDocument.SavedViewpoints.ResolveGuid(guid);
      }
    }

    var savedRef = new NAV.SavedItemReference("LcOpSavedViewsElement", savedViewReference);

    var resolvedReference = NavisworksApp.ActiveDocument.ResolveReference(savedRef) as NAV.SavedViewpoint;

    return resolvedReference
      ?? throw new SpeckleSendFilterException($"Saved view with reference {savedViewReference} not found.");
  }

  private IEnumerable<string> ResolvedSavedViewObjects(NAV.SavedViewpoint savedView)
  {
    var objectIds = new List<string>();

    // THIS IS COMMENTED OUT AS IT IS LEGACY DEFENSIVE BEHAVIOUR - DISCUSSION REQUIRED
    // if (!savedView.ContainsVisibilityOverrides)
    // {
    //   // We check this again as the view settings may have changed in the saved card.
    //   // If the saved view does not contain visibility overrides, this is effectively everything in the model.
    //   // This will need to be the documented behaviour.
    //   throw new SpeckleSendFilterException(
    //     "Saved view does not contain visibility overrides. This would effectively publish everything in the model."
    //   );
    // }

    NavisworksApp.ActiveDocument.SavedViewpoints.CurrentSavedViewpoint = savedView;
    var models = NavisworksApp.ActiveDocument.Models;
    NavisworksApp.ActiveDocument.CurrentSelection.Clear();

    foreach (var model in models)
    {
      var rootItem = model.RootItem;

      if (!_selectionService.IsVisible(rootItem))
      {
        // If the root item is hidden, we skip it and its descendants.
        continue;
      }

      objectIds.AddRange(
        rootItem.Descendants.Where(_selectionService.IsVisible).Select(_selectionService.GetModelItemPath).ToList()
      );
    }

    return objectIds;
  }

  /// <summary>
  /// Since it is called from constructor, it is re-called whenever UI calls SendBinding.GetSendFilters() on SendFilter dialog.
  /// Do not change the behavior/scope of this class on send binding unless make sure the behavior is same. Otherwise, we might not be able to update list of saved sets.
  /// </summary>
  private void GetSavedViews()
  {
    List<NAV.SavedViewpoint> savedViewRecords = [];

    var root = NavisworksApp.ActiveDocument.SavedViewpoints.RootItem;

    CollectSavedViews(root, savedViewRecords);

    Items = savedViewRecords
      .Select(viewRecord =>
      {
        var reference = NavisworksApp.ActiveDocument.SavedViewpoints.CreateReference(viewRecord);

        // If the guid is effectively empty, we can use the saved view's name as a fallback
        var selectItemId =
          viewRecord.Guid.ToString() == Guid.Empty.ToString() ? reference.SavedItemId : viewRecord.Guid.ToString();
        string hierarchicalName = SavedItemHelpers.BuildHierarchicalName(viewRecord, root);

        return new SendFilterSelectItem(selectItemId, hierarchicalName);
      })
      .ToList();
  }

  private static void CollectSavedViews(NAV.SavedItem parentItem, List<NAV.SavedViewpoint> collectedSets)
  {
    if (!parentItem.IsGroup)
    {
      return;
    }

    foreach (NAV.SavedItem item in ((NAV.GroupItem)parentItem).Children)
    {
      switch (item)
      {
        // case NAV.SavedViewpoint { ContainsVisibilityOverrides: false }:
        // Legacy defensive behaviour: skip viewpoints without visibility overrides.
        // Essentially, send everything, or whatever the current view state for hidden elements is.
        // break;
        case NAV.SavedViewpointAnimationCut:
          // Skip animation cuts.
          break;
        case NAV.SavedViewpoint savedViewpoint:
          collectedSets.Add(savedViewpoint);
          break;
        case NAV.GroupItem groupItem when groupItem.Children.Count > 0:
          CollectSavedViews(groupItem, collectedSets);
          break;
        // No action for empty groups or unknown types.
      }
    }
  }
}
