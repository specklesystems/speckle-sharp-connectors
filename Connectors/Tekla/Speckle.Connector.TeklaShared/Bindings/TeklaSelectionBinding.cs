using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Tekla.Structures;
using Tekla.Structures.Model;

namespace Speckle.Connectors.TeklaShared.Bindings;

public class TeklaSelectionBinding : ISelectionBinding
{
  private const string SELECTION_EVENT = "setSelection";
  private readonly object _selectionEventHandlerLock = new object();
  private readonly IAppIdleManager _idleManager;
  private readonly Events _events;
  private readonly Model _model;
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
    _selector = selector;
    _events = events;
    _model = new Model();

    _events.SelectionChange += OnSelectionChangeEvent;
    _events.Register();
  }

  private void OnSelectionChangeEvent()
  {
    lock (_selectionEventHandlerLock)
    {
      _idleManager.SubscribeToIdle(nameof(UpdateSelection), UpdateSelection);
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
    var objectIds = new List<string>();
    var objectTypes = new List<string>();

    ModelObjectEnumerator selectedObjects = _selector.GetSelectedObjects();
    if (selectedObjects == null)
    {
      return new SelectionInfo(new List<string>(), "No objects selected.");
    }

    while (selectedObjects.MoveNext())
    {
      ModelObject modelObject = selectedObjects.Current;
      if (modelObject?.Identifier?.GUID == null)
      {
        continue; // Skip if any part is null
      }

      string globalId = modelObject.Identifier.GUID.ToString();
      objectIds.Add(globalId);
      objectTypes.Add(modelObject.GetType().Name);
    }

    // Filter out the objects that Tekla API ignores (e.g. Construction objects with "000000.." GUID)
    List<string> filteredObjectIds = objectIds
      .Where(id => _model.SelectModelObject(new Identifier(new Guid(id))) != null)
      .ToList();

    string typesString = string.Join(", ", objectTypes.Distinct());
    return new SelectionInfo(
      filteredObjectIds,
      filteredObjectIds.Count == 0 ? "No objects selected." : $"{filteredObjectIds.Count} objects ({typesString})"
    );
  }
}
