using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.RhinoShared;
using Tekla.Structures.Model;

namespace Speckle.Connectors.TeklaShared.Bindings;

public class TeklaSelectionBinding : ISelectionBinding
{
  private readonly IAppIdleManager _idleManager;
  private const string SELECTION_EVENT = "setSelection";
  private readonly object _selectionEventHandlerLock = new object();
  private readonly Tekla.Structures.Model.UI.ModelObjectSelector _selector;
  private readonly IEventAggregator _eventAggregator;

  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }

  public TeklaSelectionBinding(
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    Tekla.Structures.Model.UI.ModelObjectSelector selector,
    IEventAggregator eventAggregator
  )
  {
    _idleManager = idleManager;
    Parent = parent;
    _selector = selector;
    _eventAggregator = eventAggregator;

    eventAggregator.GetEvent<SelectionChange>().Subscribe(_ => Events_SelectionChangeEvent());
  }

  private void Events_SelectionChangeEvent()
  {
    lock (_selectionEventHandlerLock)
    {
      _eventAggregator.GetEvent<IdleEvent>().OneTimeSubscribe(nameof(TeklaSelectionBinding), UpdateSelection);
    }
  }

  private void UpdateSelection()
  {
    SelectionInfo selInfo = GetSelection();
    Parent.Send(SELECTION_EVENT, selInfo);
  }

  public SelectionInfo GetSelection()
  {
    ModelObjectEnumerator selectedObjects = _selector.GetSelectedObjects();
    List<string> objectIds = new List<string>();
    List<string> objectTypes = new List<string>();

    while (selectedObjects.MoveNext())
    {
      ModelObject modelObject = selectedObjects.Current;
      string globalId = modelObject.Identifier.GUID.ToString();
      objectIds.Add(globalId);
      objectTypes.Add(modelObject.GetType().Name);
    }

    string typesString = string.Join(", ", objectTypes.Distinct());

    return new SelectionInfo(
      objectIds,
      objectIds.Count == 0 ? "No objects selected." : $"{objectIds.Count} objects ({typesString})"
    );
  }
}
