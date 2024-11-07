using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Tekla.Structures.Model;

namespace Speckle.Connector.Tekla2024.Bindings;

public class TeklaSelectionBinding : ISelectionBinding
{
  private readonly IAppIdleManager _idleManager;
  private const string SELECTION_EVENT = "setSelection";
  private readonly Tekla.Structures.Model.Events _events;
  private readonly object _selectionEventHandlerLock = new object();
  private readonly Tekla.Structures.Model.UI.ModelObjectSelector _selector;

  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }

  public TeklaSelectionBinding(
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    Events events,
    Tekla.Structures.Model.UI.ModelObjectSelector selector
  )
  {
    _idleManager = idleManager;
    Parent = parent;
    _events = events;
    _selector = selector;

    _events.SelectionChange += Events_SelectionChangeEvent;
    _events.Register();

    UpdateSelection();
  }

  private void Events_SelectionChangeEvent()
  {
    lock (_selectionEventHandlerLock)
    {
      _idleManager.SubscribeToIdle(nameof(TeklaSelectionBinding), UpdateSelection);
      UpdateSelection();
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
