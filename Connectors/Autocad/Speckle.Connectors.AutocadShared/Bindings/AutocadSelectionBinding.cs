using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Autocad.Bindings;

public class AutocadSelectionBinding : ISelectionBinding
{
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IThreadContext _threadContext;
  private const string SELECTION_EVENT = "setSelection";
  private readonly HashSet<Document> _visitedDocuments = new();

  public string Name => "selectionBinding";

  public IBrowserBridge Parent { get; }

  public AutocadSelectionBinding(
    IBrowserBridge parent,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IThreadContext threadContext
  )
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _threadContext = threadContext;
    Parent = parent;

    // POC: Use here Context for doc. In converters it's OK but we are still lacking to use context into bindings.
    // It is with the case of if binding created with already a document
    // This is valid when user opens acad file directly double clicking
    TryRegisterDocumentForSelection(Application.DocumentManager.MdiActiveDocument);
    Application.DocumentManager.DocumentActivated += (_, e) =>
      _topLevelExceptionHandler.CatchUnhandled(() => OnDocumentChanged(e.Document));
  }

  private void OnDocumentChanged(Document? document) => TryRegisterDocumentForSelection(document);

  private void TryRegisterDocumentForSelection(Document? document)
  {
    if (document == null)
    {
      return;
    }

    if (!_visitedDocuments.Contains(document))
    {
      document.ImpliedSelectionChanged += (_, _) =>
        _topLevelExceptionHandler.FireAndForget(async () => await _threadContext.RunOnMainAsync(OnSelectionChanged));

      _visitedDocuments.Add(document);
    }
  }

  // NOTE: Autocad 2022 caused problems, so we need to refactor things a bit in here to always store
  // selection info locally (and get it updated by the event, which we can control to run on the main thread).
  // Ui requests to GetSelection() should just return this local copy that is kept up to date by the event handler.
  private SelectionInfo _selectionInfo;

  private async Task OnSelectionChanged()
  {
    _selectionInfo = GetSelectionInternal();
    await Parent.Send(SELECTION_EVENT, _selectionInfo);
  }

  public SelectionInfo GetSelection() => _selectionInfo;

  private SelectionInfo GetSelectionInternal()
  {
    // POC: Will be addressed to move it into AutocadContext! https://spockle.atlassian.net/browse/CNX-9319
    Document? doc = Application.DocumentManager.MdiActiveDocument;
    List<string> objs = new();
    List<string> objectTypes = new();
    if (doc != null)
    {
      using var tr = doc.TransactionManager.StartTransaction();
      PromptSelectionResult selection = doc.Editor.SelectImplied();
      if (selection.Status == PromptStatus.OK)
      {
        foreach (SelectedObject obj in selection.Value)
        {
          var dbObject = tr.GetObject(obj.ObjectId, OpenMode.ForRead);
          if (dbObject == null)
          {
            continue;
          }

          objectTypes.Add(dbObject.GetType().Name);
          objs.Add(dbObject.GetSpeckleApplicationId());

          // do the same also for each AttributeReference inside the BlockReference (attribute change is not affecting the block otherwise)
          if (dbObject is BlockReference blockReference)
          {
            foreach (ObjectId id in blockReference.AttributeCollection)
            {
              var attr = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
              objectTypes.Add(attr.GetType().Name);
              objs.Add(attr.GetSpeckleApplicationId());
            }
          }
        }

        tr.Commit();
      }
    }
    List<string> flatObjectTypes = objectTypes.Select(o => o).Distinct().ToList();
    return new SelectionInfo(objs, $"{objs.Count} objects ({string.Join(", ", flatObjectTypes)})");
  }
}
