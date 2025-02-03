using Speckle.Connectors.CSiShared.Events;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.Utils;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.CSiShared.Bindings;

public sealed class CsiSharedSelectionBinding : ISelectionBinding
{
  private readonly ICsiApplicationService _csiApplicationService;
  private HashSet<string> _lastSelection = new();

  public IBrowserBridge Parent { get; }
  public string Name => "selectionBinding";

  public CsiSharedSelectionBinding(
    IBrowserBridge parent,
    ICsiApplicationService csiApplicationService,
    IEventAggregator eventAggregator
  )
  {
    Parent = parent;
    _csiApplicationService = csiApplicationService;

    eventAggregator.GetEvent<SelectionBindingEvent>().SubscribePeriodic(TimeSpan.FromSeconds(1), CheckSelectionChanged);
  }

  private void CheckSelectionChanged(object _)
  {
    var currentSelection = GetSelection();
    var currentIds = new HashSet<string>(currentSelection.SelectedObjectIds);

    if (!_lastSelection.SetEquals(currentIds))
    {
      _lastSelection = currentIds;
      Parent.Send(SelectionBindingEvents.SET_SELECTION, currentSelection);
    }
  }

  /// <summary>
  /// Gets the selection and creates an encoded ID (objectType and objectName).
  /// </summary>
  /// <remarks>
  /// Refer to ObjectIdentifier.cs for more info.
  /// </remarks>
  public SelectionInfo GetSelection()
  {
    int numberItems = 0;
    int[] objectType = [];
    string[] objectName = [];

    _csiApplicationService.SapModel.SelectObj.GetSelected(ref numberItems, ref objectType, ref objectName);

    var encodedIds = new List<string>(numberItems);
    var typeCounts = new Dictionary<string, int>();

    for (int i = 0; i < numberItems; i++)
    {
      var typeKey = (ModelObjectType)objectType[i];
      var typeName = typeKey.ToString();

      encodedIds.Add(ObjectIdentifier.Encode(objectType[i], objectName[i]));
      typeCounts[typeName] = (typeCounts.TryGetValue(typeName, out var count) ? count : 0) + 1; // NOTE: Cross-framework compatibility (net 48 and net8)
    }

    var summary =
      encodedIds.Count == 0
        ? "No objects selected."
        : $"{encodedIds.Count} objects ({string.Join(", ", 
            typeCounts.Select(kv => $"{kv.Value} {kv.Key}"))})";

    return new SelectionInfo(encodedIds, summary);
  }
}
