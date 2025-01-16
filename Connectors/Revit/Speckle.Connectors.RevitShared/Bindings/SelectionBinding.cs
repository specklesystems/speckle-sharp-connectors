using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Bindings;

// POC: we need a base a RevitBaseBinding
internal sealed class SelectionBinding : RevitBaseBinding, ISelectionBinding, IDisposable
{
#if REVIT2022
  private readonly System.Timers.Timer _selectionTimer;
#endif

  public SelectionBinding(
    RevitContext revitContext,
    DocumentModelStore store,
    IAppIdleManager revitIdleManager,
    IBrowserBridge parent
  )
    : base("selectionBinding", store, parent, revitContext)
  {
#if REVIT2022
    // NOTE: getting the selection data should be a fast function all, even for '000s of elements - and having a timer hitting it every 1s is ok.
    _selectionTimer = new System.Timers.Timer(1000);
    _selectionTimer.Elapsed += (_, _) => parent.TopLevelExceptionHandler.CatchUnhandled(OnSelectionChanged);
    _selectionTimer.Start();
#else

    RevitContext.UIApplication.NotNull().SelectionChanged += (_, _) =>
      revitIdleManager.SubscribeToIdle(nameof(SelectionBinding), OnSelectionChanged);
#endif
  }

  private void OnSelectionChanged()
  {
    if (RevitContext.UIApplication == null || RevitContext.UIApplication.ActiveUIDocument == null)
    {
      return;
    }
    Parent.Send(SelectionBindingEvents.SET_SELECTION, GetSelection());
  }

  public SelectionInfo GetSelection()
  {
    if (RevitContext.UIApplication == null || RevitContext.UIApplication.ActiveUIDocument == null)
    {
      return new SelectionInfo(Array.Empty<string>(), "No objects selected.");
    }

    var activeUIDoc = RevitContext.UIApplication.ActiveUIDocument.NotNull();

    // POC: this was also being called on shutdown
    // probably the bridge needs to be able to know if the plugin has been terminated
    // also on termination the OnSelectionChanged event needs unwinding
    var selectionIds = activeUIDoc
      .Selection.GetElementIds()
      .Select(eid => activeUIDoc.Document.GetElement(eid).UniqueId.ToString())
      .ToList();
    return new SelectionInfo(selectionIds, $"{selectionIds.Count} objects selected.");
  }

  public void Dispose()
  {
#if REVIT2022
    _selectionTimer.Dispose();
#endif
  }
}
