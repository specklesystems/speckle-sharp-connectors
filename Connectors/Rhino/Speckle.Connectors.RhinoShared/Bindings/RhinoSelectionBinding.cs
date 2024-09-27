using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.Rhino.HostApp;

namespace Speckle.Connectors.Rhino.Bindings;

public sealed class RhinoSelectionBinding(IRhinoIdleManager idleManager, IBrowserBridge parent)
  : ISelectionBinding,
    IPostInitBinding,
    IDisposable
{
  private const string SELECTION_EVENT = "setSelection";

  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; } = parent;

  public void PostInitialization()
  {
    RhinoDoc.SelectObjects += OnSelectionChange;
    RhinoDoc.DeselectObjects += OnSelectionChange;
    RhinoDoc.DeselectAllObjects += OnSelectionChange;
  }

  public void Dispose()
  {
    RhinoDoc.SelectObjects -= OnSelectionChange;
    RhinoDoc.DeselectObjects -= OnSelectionChange;
    RhinoDoc.DeselectAllObjects -= OnSelectionChange;
  }

  private void OnSelectionChange(object? o, EventArgs eventArgs) =>
    idleManager.SubscribeToIdle(nameof(RhinoSelectionBinding), UpdateSelection);

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
