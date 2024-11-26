using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.RevitShared;
using Speckle.Connectors.RevitShared.Operations.Send.Filters;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Bindings;

internal sealed class BasicConnectorBindingRevit : IBasicConnectorBinding
{
  // POC: name and bridge might be better for them to be protected props?
  public string Name { get; private set; }
  public IBrowserBridge Parent { get; private set; }

  public BasicConnectorBindingCommands Commands { get; }

  private readonly APIContext _apiContext;
  private readonly DocumentModelStore _store;
  private readonly RevitContext _revitContext;
  private readonly ISpeckleApplication _speckleApplication;

  public BasicConnectorBindingRevit(
    APIContext apiContext,
    DocumentModelStore store,
    IBrowserBridge parent,
    RevitContext revitContext,
    ISpeckleApplication speckleApplication
  )
  {
    Name = "baseBinding";
    Parent = parent;
    _apiContext = apiContext;
    _store = store;
    _revitContext = revitContext;
    _speckleApplication = speckleApplication;
    Commands = new BasicConnectorBindingCommands(parent);

    // POC: event binding?
    _store.DocumentChanged += (_, _) =>
      parent.TopLevelExceptionHandler.FireAndForget(async () =>
      {
        await Commands.NotifyDocumentChanged().ConfigureAwait(false);
      });
  }

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public DocumentInfo? GetDocumentInfo()
  {
    // POC: not sure why this would ever be null, is this needed?
    _revitContext.UIApplication.NotNull();

    var doc = _revitContext.UIApplication.ActiveUIDocument?.Document;
    if (doc is null)
    {
      return null;
    }

    if (doc.IsFamilyDocument)
    {
      return new DocumentInfo("", "", "") { Message = "Family environment files not supported by Speckle." };
    }

    var info = new DocumentInfo(doc.PathName, doc.Title, doc.GetHashCode().ToString());

    return info;
  }

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.AddModel(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public async Task HighlightModel(string modelCardId)
  {
    var model = _store.GetModelById(modelCardId);
    var activeUIDoc =
      _revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    var elementIds = new List<ElementId>();

    if (model is SenderModelCard senderModelCard)
    {
      if (senderModelCard.SendFilter is IRevitSendFilter revitFilter)
      {
        revitFilter.SetContext(_revitContext, _apiContext);
      }

      if (senderModelCard.SendFilter is RevitViewsFilter revitViewsFilter)
      {
        await _apiContext
          .Run(() =>
          {
            var view = revitViewsFilter.GetView();
            if (view is not null)
            {
              _revitContext.UIApplication.ActiveUIDocument.ActiveView = view;
            }
          })
          .ConfigureAwait(false);
        return;
      }

      var selectedObjects = senderModelCard.SendFilter.NotNull().SelectedObjectIds;

      elementIds = selectedObjects
        .Select(uid => ElementIdHelper.GetElementIdFromUniqueId(activeUIDoc.Document, uid))
        .Where(el => el is not null)
        .Cast<ElementId>()
        .ToList();
    }

    if (model is ReceiverModelCard receiverModelCard)
    {
      elementIds = receiverModelCard
        .BakedObjectIds.NotNull()
        .Select(uid => ElementIdHelper.GetElementIdFromUniqueId(activeUIDoc.Document, uid))
        .Where(el => el is not null)
        .Cast<ElementId>()
        .ToList();
    }

    if (elementIds.Count == 0)
    {
      await Commands
        .SetModelError(modelCardId, new InvalidOperationException("No objects found to highlight."))
        .ConfigureAwait(false);
      return;
    }

    HighlightObjectsOnView(elementIds);
  }

  /// <summary>
  /// Highlights the objects from the given ids.
  /// </summary>
  /// <param name="objectIds"> UniqueId's of the DB.Elements.</param>
  public Task HighlightObjects(IReadOnlyList<string> objectIds)
  {
    var activeUIDoc =
      _revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    HighlightObjectsOnView(
      objectIds
        .Select(uid => ElementIdHelper.GetElementIdFromUniqueId(activeUIDoc.Document, uid))
        .Where(el => el is not null)
        .Cast<ElementId>()
        .ToList()
    );
    return Task.CompletedTask;
  }

  private void HighlightObjectsOnView(List<ElementId> objectIds)
  {
    // POC: don't know if we can rely on storing the ActiveUIDocument, hence getting it each time
    var activeUIDoc =
      _revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    activeUIDoc.Selection.SetElementIds(objectIds);
    activeUIDoc.ShowElements(objectIds);
    ;
  }
}
