using System.Reflection;
using Autodesk.Revit.DB;
using Revit.Async;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.RevitShared;
using Speckle.Connectors.Utils.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Bindings;

internal sealed class BasicConnectorBindingRevit : IBasicConnectorBinding
{
  // POC: name and bridge might be better for them to be protected props?
  public string Name { get; private set; }
  public IBridge Parent { get; private set; }

  public BasicConnectorBindingCommands Commands { get; }

  private readonly DocumentModelStore _store;
  private readonly RevitContext _revitContext;

  public BasicConnectorBindingRevit(DocumentModelStore store, IBridge parent, RevitContext revitContext)
  {
    Name = "baseBinding";
    Parent = parent;
    _store = store;
    _revitContext = revitContext;
    Commands = new BasicConnectorBindingCommands(parent);

    // POC: event binding?
    _store.DocumentChanged += (_, _) =>
      parent.TopLevelExceptionHandler.FireAndForget(async () =>
      {
        await Commands.NotifyDocumentChanged().ConfigureAwait(false);
      });
  }

  public string GetConnectorVersion() => Assembly.GetAssembly(GetType()).NotNull().GetVersion();

  public string GetSourceApplicationName() => Speckle.Connectors.Utils.Connector.Slug.ToLower(); // POC: maybe not right place but... // ANOTHER POC: We should align this naming from somewhere in common DUI projects instead old structs. I know there are other POC comments around this

  public string GetSourceApplicationVersion() => Speckle.Connectors.Utils.Connector.VersionString; // POC: maybe not right place but...

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
    SenderModelCard model = (SenderModelCard)_store.GetModelById(modelCardId);

    var activeUIDoc =
      _revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    var elementIds = model
      .SendFilter.NotNull()
      .GetObjectIds()
      .Select(uid => ElementIdHelper.GetElementIdFromUniqueId(activeUIDoc.Document, uid))
      .ToList();

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
        objectIds.Select(uid => ElementIdHelper.GetElementIdFromUniqueId(activeUIDoc.Document, uid)).ToList()
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
