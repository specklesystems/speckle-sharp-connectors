using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Bindings;

// POC: we need a base a RevitBaseBinding
internal sealed class SelectionBinding : RevitBaseBinding, ISelectionBinding
{
  private readonly IRevitContext _revitContext;

  public SelectionBinding(IRevitContext revitContext, IBrowserBridge parent, IEventAggregator eventAggregator)
    : base("selectionBinding", parent)
  {
    _revitContext = revitContext;
    eventAggregator.GetEvent<SelectionChangedEvent>().Subscribe(OnSelectionChanged);
  }

  private void OnSelectionChanged(object _)
  {
    if (_revitContext.UIApplication.ActiveUIDocument == null)
    {
      return;
    }
    Parent.Send(SelectionBindingEvents.SET_SELECTION, GetSelection());
  }

  public SelectionInfo GetSelection()
  {
    if (_revitContext.UIApplication.ActiveUIDocument == null)
    {
      return new SelectionInfo(Array.Empty<string>(), "No objects selected.");
    }

    var activeUIDoc = _revitContext.UIApplication.ActiveUIDocument.NotNull();

    // POC: this was also being called on shutdown
    // probably the bridge needs to be able to know if the plugin has been terminated
    // also on termination the OnSelectionChanged event needs unwinding
    var selectionIds = activeUIDoc
      .Selection.GetElementIds()
      .Select(eid => activeUIDoc.Document.GetElement(eid).UniqueId.ToString())
      .ToList();
    return new SelectionInfo(selectionIds, $"{selectionIds.Count} objects selected.");
  }
}
