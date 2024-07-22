using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;

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
    IRevitIdleManager revitIdleManager,
    IBridge bridge,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base("selectionBinding", store, bridge, revitContext)
  {
#if !REVIT2022
    RevitContext.UIApplication.NotNull().SelectionChanged += (_, _) =>
      revitIdleManager.SubscribeToIdle(nameof(SelectionBinding), OnSelectionChanged);
#else
    // NOTE: getting the selection data should be a fast function all, even for '000s of elements - and having a timer hitting it every 1s is ok.
    _selectionTimer = new System.Timers.Timer(1000);
    _selectionTimer.Elapsed += (_, _) => topLevelExceptionHandler.CatchUnhandled(OnSelectionChanged);
    _selectionTimer.Start();
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

    // POC: this was also being called on shutdown
    // probably the bridge needs to be able to know if the plugin has been terminated
    // also on termination the OnSelectionChanged event needs unwinding
    var selectionIds = (RevitContext.UIApplication?.ActiveUIDocument?.Selection.GetElementIds())
      .NotNull()
      .Select(id => id.ToString())
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
