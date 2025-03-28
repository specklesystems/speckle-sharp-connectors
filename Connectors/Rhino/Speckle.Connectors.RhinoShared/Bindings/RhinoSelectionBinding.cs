using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Rhino.Bindings;

public class RhinoSelectionBinding : ISelectionBinding
{
  private readonly IAppIdleManager _idleManager;
  private const string SELECTION_EVENT = "setSelection";

  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }

  public RhinoSelectionBinding(IAppIdleManager idleManager, IBrowserBridge parent)
  {
    _idleManager = idleManager;
    Parent = parent;

    RhinoDoc.SelectObjects += OnSelectionChange;
    RhinoDoc.DeselectObjects += OnSelectionChange;
    RhinoDoc.DeselectAllObjects += OnSelectionChange;
  }

  private void OnSelectionChange(object? o, EventArgs eventArgs) =>
    _idleManager.SubscribeToIdle(nameof(UpdateSelection), UpdateSelection);

  private void UpdateSelection()
  {
    SelectionInfo selInfo = GetSelection();
    Parent.Send(SELECTION_EVENT, selInfo);
  }

  public SelectionInfo GetSelection()
  {
    List<RhinoObject> objects = RhinoDoc.ActiveDoc.Objects.GetSelectedObjects(false, false).ToList();
    List<string> objectIds = objects.Select(o => o.Id.ToString()).ToList();
    int layerCount = objects.Select(o => o.Attributes.LayerIndex).Distinct().Count();
    List<string> objectTypes = objects.Select(o => o.ObjectType.ToString()).Distinct().ToList();
    return new SelectionInfo(
      objectIds,
      $"{objectIds.Count} objects ({string.Join(", ", objectTypes)}) from {layerCount} layer{(layerCount != 1 ? "s" : "")}"
    );
  }
}
