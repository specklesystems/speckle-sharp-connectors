using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Revit.Async;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.RevitShared;
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

  private readonly DocumentModelStore _store;
  private readonly RevitContext _revitContext;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ILogger<BasicConnectorBindingRevit> _logger;

  public BasicConnectorBindingRevit(
    DocumentModelStore store,
    IBrowserBridge parent,
    RevitContext revitContext,
    ISpeckleApplication speckleApplication,
    ILogger<BasicConnectorBindingRevit> logger
  )
  {
    Name = "baseBinding";
    Parent = parent;
    _store = store;
    _revitContext = revitContext;
    _speckleApplication = speckleApplication;
    _logger = logger;
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

  public void AddModel(ModelCard model) => _store.Models.Add(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public async Task HighlightModel(string modelCardId)
  {
    var model = _store.GetModelById(modelCardId);

    if (model is null)
    {
      _logger.LogError("Model was null when highlighting received model");
      return;
    }

    var activeUIDoc =
      _revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    var elementIds = new List<ElementId>();

    if (model is SenderModelCard senderModelCard)
    {
      elementIds = senderModelCard
        .SendFilter.NotNull()
        .GetObjectIds()
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

    await HighlightObjectsOnView(elementIds).ConfigureAwait(false);
  }

  /// <summary>
  /// Highlights the objects from the given ids.
  /// </summary>
  /// <param name="objectIds"> UniqueId's of the DB.Elements.</param>
  public async Task HighlightObjects(IReadOnlyList<string> objectIds)
  {
    var activeUIDoc =
      _revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    await HighlightObjectsOnView(
        objectIds
          .Select(uid => ElementIdHelper.GetElementIdFromUniqueId(activeUIDoc.Document, uid))
          .Where(el => el is not null)
          .Cast<ElementId>()
          .ToList()
      )
      .ConfigureAwait(false);
    ;
  }

  private async Task HighlightObjectsOnView(List<ElementId> objectIds)
  {
    // POC: don't know if we can rely on storing the ActiveUIDocument, hence getting it each time
    var activeUIDoc =
      _revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    // UiDocument operations should be wrapped into RevitTask, otherwise doesn't work on other tasks.
    await RevitTask
      .RunAsync(() =>
      {
        activeUIDoc.Selection.SetElementIds(objectIds);
        activeUIDoc.ShowElements(objectIds);
      })
      .ConfigureAwait(false);
    ;
  }
}
