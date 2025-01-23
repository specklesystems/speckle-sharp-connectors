using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Plugin;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connectors.Autocad.Bindings;

public class AutocadSelectionBinding : ISelectionBinding
{
  private const string SELECTION_EVENT = "setSelection";
  private readonly IEventAggregator _eventAggregator;
  private readonly HashSet<string> _visitedDocuments = new();

  public string Name => "selectionBinding";

  public IBrowserBridge Parent { get; }

  public AutocadSelectionBinding(IBrowserBridge parent, IEventAggregator eventAggregator)
  {
    _eventAggregator = eventAggregator;
    Parent = parent;

    // POC: Use here Context for doc. In converters it's OK but we are still lacking to use context into bindings.
    // It is with the case of if binding created with already a document
    // This is valid when user opens acad file directly double clicking
    TryRegisterDocumentForSelection(Application.DocumentManager.MdiActiveDocument);
    eventAggregator.GetEvent<DocumentActivatedEvent>().Subscribe(OnDocumentChanged);
    eventAggregator.GetEvent<ImpliedSelectionChangedEvent>().Subscribe(OnSelectionChanged);
    eventAggregator.GetEvent<DocumentToBeDestroyedEvent>().Subscribe(OnDocumentDestroyed);
  }

  private void OnDocumentDestroyed(DocumentCollectionEventArgs e)
  {
    if (!_visitedDocuments.Contains(e.Document.Name))
    {
      e.Document.ImpliedSelectionChanged -= DocumentOnImpliedSelectionChanged;
      _visitedDocuments.Remove(e.Document.Name);
    }
  }

  private void OnDocumentChanged(DocumentCollectionEventArgs e) => TryRegisterDocumentForSelection(e.Document);

  private void TryRegisterDocumentForSelection(Document? document)
  {
    if (document == null)
    {
      return;
    }

    if (!_visitedDocuments.Contains(document.Name))
    {
      document.ImpliedSelectionChanged += DocumentOnImpliedSelectionChanged;

      _visitedDocuments.Add(document.Name);
    }
  }

  // ReSharper disable once AsyncVoidMethod
  private async void DocumentOnImpliedSelectionChanged(object? sender, EventArgs e) =>
    await _eventAggregator.GetEvent<ImpliedSelectionChangedEvent>().PublishAsync(e);

  // NOTE: Autocad 2022 caused problems, so we need to refactor things a bit in here to always store
  // selection info locally (and get it updated by the event, which we can control to run on the main thread).
  // Ui requests to GetSelection() should just return this local copy that is kept up to date by the event handler.
  private SelectionInfo _selectionInfo;

  private async Task OnSelectionChanged(EventArgs _)
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
        }

        tr.Commit();
      }
    }
    List<string> flatObjectTypes = objectTypes.Select(o => o).Distinct().ToList();
    return new SelectionInfo(objs, $"{objs.Count} objects ({string.Join(", ", flatObjectTypes)})");
  }
}
