using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Bindings;

// POC: we need a base a RevitBaseBinding
internal sealed class SelectionBinding : RevitBaseBinding, ISelectionBinding, IDisposable
{
#if REVIT2022
  private readonly System.Timers.Timer _selectionTimer;
#endif
  private readonly RevitContext _revitContext;
  private readonly IAppIdleManager _idleManager;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  public SelectionBinding(
    RevitContext revitContext,
    IBrowserBridge parent,
    IAppIdleManager idleManager,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base("selectionBinding", parent)
  {
    _revitContext = revitContext;
    _idleManager = idleManager;
    _topLevelExceptionHandler = topLevelExceptionHandler;

#if REVIT2022
    // NOTE: getting the selection data should be a fast function all, even for '000s of elements - and having a timer hitting it every 1s is ok.
    _selectionTimer = new System.Timers.Timer(1000);
    _selectionTimer.Elapsed += (_, _) => _topLevelExceptionHandler.CatchUnhandled(OnSelectionChanged);
    _selectionTimer.Start();
#else

    _revitContext.UIApplication.NotNull().SelectionChanged += (_, _) =>
      _idleManager.SubscribeToIdle(nameof(OnSelectionChanged), OnSelectionChanged);
#endif
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

  public void Dispose()
  {
#if REVIT2022
    _selectionTimer.Dispose();
#endif
  }
}
