using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Utils.Common;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Autocad.Bindings;

public class AutocadBasicConnectorBinding : IBasicConnectorBinding
{
  private readonly IAccountManager _accountManager;
  public string Name { get; set; } = "baseBinding";
  public IBridge Parent { get; }

  private readonly DocumentModelStore _store;

  public BasicConnectorBindingCommands Commands { get; }

  public AutocadBasicConnectorBinding(DocumentModelStore store, IBridge parent, IAccountManager accountManager)
  {
    _store = store;
    Parent = parent;
    _accountManager = accountManager;
    Commands = new BasicConnectorBindingCommands(parent);
    _store.DocumentChanged += (_, _) =>
    {
      Commands.NotifyDocumentChanged();
    };
  }

  public string GetConnectorVersion() => typeof(AutocadBasicConnectorBinding).Assembly.GetVersion();

  public string GetSourceApplicationName() => Utils.Connector.Slug;

  public string GetSourceApplicationVersion() => Utils.Connector.VersionString;

  public Account[] GetAccounts() => _accountManager.GetAccounts().ToArray();

  public DocumentInfo? GetDocumentInfo()
  {
    // POC: Will be addressed to move it into AutocadContext!
    var doc = Application.DocumentManager.MdiActiveDocument;
    if (doc is null)
    {
      return null;
    }
    string name = doc.Name.Split(System.IO.Path.PathSeparator).Last();
    return new DocumentInfo(doc.Name, name, doc.GetHashCode().ToString());
  }

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.Models.Add(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public void HighlightObjects(List<string> objectIds)
  {
    // POC: Will be addressed to move it into AutocadContext!
    var doc = Application.DocumentManager.MdiActiveDocument;

    var dbObjects = doc.GetObjects(objectIds);
    var acadObjectIds = dbObjects.Select(tuple => tuple.Root.Id).ToArray();
    HighlightObjectsOnView(acadObjectIds);
  }

  public void HighlightModel(string modelCardId)
  {
    // POC: Will be addressed to move it into AutocadContext!
    var doc = Application.DocumentManager.MdiActiveDocument;

    if (doc == null)
    {
      return;
    }

    var objectIds = Array.Empty<ObjectId>();

    var model = _store.GetModelById(modelCardId);
    if (model == null)
    {
      return;
    }

    if (model is SenderModelCard senderModelCard)
    {
      var dbObjects = doc.GetObjects(senderModelCard.SendFilter.NotNull().GetObjectIds());
      objectIds = dbObjects.Select(tuple => tuple.Root.Id).ToArray();
    }

    if (model is ReceiverModelCard receiverModelCard)
    {
      var dbObjects = doc.GetObjects(receiverModelCard.BakedObjectIds.NotNull());
      objectIds = dbObjects.Select(tuple => tuple.Root.Id).ToArray();
    }

    if (objectIds.Length == 0)
    {
      Commands.SetModelError(modelCardId, new OperationCanceledException("No objects found to highlight."));
      return;
    }

    HighlightObjectsOnView(objectIds, modelCardId);
  }

  private void HighlightObjectsOnView(ObjectId[] objectIds, string? modelCardId = null)
  {
    var doc = Application.DocumentManager.MdiActiveDocument;

    Parent.RunOnMainThread(() =>
    {
      try
      {
        doc.Editor.SetImpliedSelection(Array.Empty<ObjectId>()); // Deselects
        try
        {
          doc.Editor.SetImpliedSelection(objectIds);
        }
        catch (Exception e) when (!e.IsFatal())
        {
          // SWALLOW REASON:
          // If the objects under the blocks, it won't be able to select them.
          // If we try, API will throw the invalid input error, because we request something from API that Autocad doesn't
          // handle it on its current canvas. Block elements only selectable when in its scope.
        }
        doc.Editor.UpdateScreen();

        Extents3d selectedExtents = new();

        var tr = doc.TransactionManager.StartTransaction();
        foreach (ObjectId objectId in objectIds)
        {
          try
          {
            var entity = (Entity?)tr.GetObject(objectId, OpenMode.ForRead);
            if (entity?.GeometricExtents != null)
            {
              selectedExtents.AddExtents(entity.GeometricExtents);
            }
          }
          catch (Exception e) when (!e.IsFatal())
          {
            // Note: we're swallowing exeptions here because of a weird case when receiving blocks, we would have
            // acad api throw an error on accessing entity.GeometricExtents.
          }
        }

        doc.Editor.Zoom(selectedExtents);
        tr.Commit();
        Autodesk.AutoCAD.Internal.Utils.FlushGraphics();
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        if (modelCardId != null)
        {
          Commands.SetModelError(modelCardId, new OperationCanceledException("Failed to highlight objects."));
        }
        else
        {
          // This will happen, in some cases, where we highlight individual objects. Should be caught by the top level handler and not
          // crash the host app.
          throw;
        }
      }
    });
  }
}
