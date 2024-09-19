using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Common;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Rhino.Bindings;

public class RhinoBasicConnectorBinding : IBasicConnectorBinding
{
  public string Name => "baseBinding";
  public IBridge Parent { get; }
  public BasicConnectorBindingCommands Commands { get; }

  private readonly DocumentModelStore _store;
  private readonly ISpeckleApplication _speckleApplication;

  public RhinoBasicConnectorBinding(DocumentModelStore store, IBridge parent, ISpeckleApplication speckleApplication)
  {
    _store = store;
    Parent = parent;
    _speckleApplication = speckleApplication;
    Commands = new BasicConnectorBindingCommands(parent);

    _store.DocumentChanged += (_, _) =>
    {
      Commands.NotifyDocumentChanged();
    };
  }

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.AppVersion;

  public DocumentInfo? GetDocumentInfo()
  {
    if (RhinoDoc.ActiveDoc is null)
    {
      return null;
    }
    return new(RhinoDoc.ActiveDoc.Path, RhinoDoc.ActiveDoc.Name, RhinoDoc.ActiveDoc.RuntimeSerialNumber.ToString());
  }

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.Models.Add(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public void HighlightObjects(List<string> objectIds)
  {
    var objects = GetObjectsFromIds(objectIds);

    if (objects.rhinoObjects.Count == 0 && objects.groups.Count == 0)
    {
      throw new InvalidOperationException(
        "Highlighting RhinoObject is not successful.",
        new ArgumentException($"{objectIds} is not a valid id", nameof(objectIds))
      );
    }

    HighlightObjectsOnView(objects.rhinoObjects, objects.groups);
  }

  public void HighlightModel(string modelCardId)
  {
    var objectIds = new List<string>();
    var myModel = _store.GetModelById(modelCardId);

    if (myModel is SenderModelCard sender)
    {
      objectIds = sender.SendFilter.NotNull().GetObjectIds();
    }

    if (myModel is ReceiverModelCard receiver && receiver.BakedObjectIds != null)
    {
      objectIds = receiver.BakedObjectIds;
    }

    if (objectIds.Count == 0)
    {
      Commands.SetModelError(modelCardId, new OperationCanceledException("No objects found to highlight."));
      return;
    }

    var objects = GetObjectsFromIds(objectIds);

    RhinoDoc.ActiveDoc.Objects.UnselectAll();

    if (objects.rhinoObjects.Count == 0 && objects.groups.Count == 0)
    {
      Commands.SetModelError(modelCardId, new OperationCanceledException("No objects found to highlight."));
      return;
    }

    HighlightObjectsOnView(objects.rhinoObjects, objects.groups);
  }

  private (List<RhinoObject> rhinoObjects, List<Group> groups) GetObjectsFromIds(List<string> objectIds)
  {
    List<RhinoObject> rhinoObjects = objectIds
      .Select((id) => RhinoDoc.ActiveDoc.Objects.FindId(new Guid(id)))
      .Where(o => o != null)
      .ToList();

    // POC: On receive we group objects if return multiple objects
    List<Group> groups = objectIds
      .Select((id) => RhinoDoc.ActiveDoc.Groups.FindId(new Guid(id)))
      .Where(o => o != null)
      .ToList();

    return (rhinoObjects, groups);
  }

  private void HighlightObjectsOnView(IReadOnlyList<RhinoObject> rhinoObjects, IReadOnlyList<Group> groups)
  {
    RhinoDoc.ActiveDoc.Objects.UnselectAll();
    List<RhinoObject> rhinoObjectsToSelect = new(rhinoObjects);

    foreach (Group group in groups) // This is not performant if we have many groups. That's why we do not store group ids on baked object ids, to not have a problem later on highlighting all model. Mostly for single group highlight from report item.
    {
      int groupIndex = RhinoDoc.ActiveDoc.Groups.Find(group.Name);
      if (groupIndex < 0)
      {
        continue;
      }
      var sbr = RhinoDoc.ActiveDoc.Objects.Where(o =>
        o.GetGroupList() != null && o.GetGroupList().Contains(groupIndex)
      );
      rhinoObjectsToSelect.AddRange(sbr);
    }
    RhinoDoc.ActiveDoc.Objects.Select(rhinoObjectsToSelect.Select(o => o.Id));

    // Calculate the bounding box of the selected objects
    BoundingBox boundingBox = BoundingBoxExtensions.UnionRhinoObjects(rhinoObjectsToSelect);

    // Zoom to the calculated bounding box
    if (boundingBox.IsValid)
    {
      RhinoDoc.ActiveDoc.Views.ActiveView.ActiveViewport.ZoomBoundingBox(boundingBox);
    }

    RhinoDoc.ActiveDoc.Views.Redraw();
  }
}
