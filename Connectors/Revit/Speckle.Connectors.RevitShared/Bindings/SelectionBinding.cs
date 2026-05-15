using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Bindings;

// POC: we need a base a RevitBaseBinding
internal sealed class SelectionBinding : RevitBaseBinding, ISelectionBinding
{
  private readonly RevitContext _revitContext;

  public SelectionBinding(
    RevitContext revitContext,
    IBrowserBridge parent,
    RevitIdleManager idleManager,
    IRevitTask revitTask,
    IConfigStore configStore
  )
    : base("selectionBinding", parent)
  {
    _revitContext = revitContext;

    if (!configStore.GetConnectorConfig().SelectionChangeListeningDisabled)
    {
      revitTask.Run(() =>
        _revitContext.UIApplication.NotNull().SelectionChanged += (_, _) =>
          idleManager.SubscribeToIdle(nameof(OnSelectionChanged), OnSelectionChanged)
      );
    }
  }

  private void OnSelectionChanged()
  {
    if (_revitContext.UIApplication.NotNull().ActiveUIDocument == null)
    {
      return;
    }
    Parent.Send(SelectionBindingEvents.SET_SELECTION, GetSelection());
  }

  public SelectionInfo GetSelection()
  {
    if (_revitContext.UIApplication.NotNull().ActiveUIDocument == null)
    {
      return new SelectionInfo(Array.Empty<string>(), "No objects selected.");
    }

    var activeUIDoc = _revitContext.UIApplication.ActiveUIDocument.NotNull();
    var doc = activeUIDoc.Document;

    // POC: this was also being called on shutdown
    // probably the bridge needs to be able to know if the plugin has been terminated
    // also on termination the OnSelectionChanged event needs unwinding
    var selectionIds = activeUIDoc.Selection.GetElementIds();
    //reduce allocates by allocating what we need.
    var selectionUniqueIds = new List<string>(selectionIds.Count);
    selectionUniqueIds.AddRange(selectionIds.Select(eid => doc.GetElement(eid).UniqueId));
    return new SelectionInfo(selectionUniqueIds, $"{selectionIds.Count} objects selected.");
  }
}
