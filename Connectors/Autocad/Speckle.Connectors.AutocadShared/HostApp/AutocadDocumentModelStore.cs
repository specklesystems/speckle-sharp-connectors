using Autodesk.AutoCAD.ApplicationServices;
using Speckle.Connectors.Autocad.Plugin;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.Autocad.HostApp;

public class AutocadDocumentStore : DocumentModelStore
{
  private const string NULL_DOCUMENT_NAME = "Null Doc";
  private string _previousDocName;
  private readonly AutocadDocumentManager _autocadDocumentManager;
  private readonly IEventAggregator _eventAggregator;

  public AutocadDocumentStore(
    IJsonSerializer jsonSerializer,
    AutocadDocumentManager autocadDocumentManager,
    IEventAggregator eventAggregator
  )
    : base(jsonSerializer)
  {
    _autocadDocumentManager = autocadDocumentManager;
    _eventAggregator = eventAggregator;
    _previousDocName = NULL_DOCUMENT_NAME;

    eventAggregator.GetEvent<DocumentActivatedEvent>().Subscribe(DocChanged);

    // since below event triggered as secondary, it breaks the logic in OnDocChangeInternal function, leaving it here for now.
    // Autodesk.AutoCAD.ApplicationServices.Application.DocumentWindowCollection.DocumentWindowActivated += (_, args) =>
    //  OnDocChangeInternal((Document)args.DocumentWindow.Document);
  }

  public override async Task OnDocumentStoreInitialized()
  {
    // POC: Will be addressed to move it into AutocadContext!
    if (Application.DocumentManager.MdiActiveDocument != null)
    {
      IsDocumentInit = true;
      // POC: this logic might go when we have document management in context
      // It is with the case of if binding created with already a document
      // This is valid when user opens acad file directly double clicking
      await TryDocChanged(Application.DocumentManager.MdiActiveDocument);
    }
  }

  private async Task DocChanged(DocumentCollectionEventArgs e) => await TryDocChanged(e.Document);

  private async Task TryDocChanged(Document? doc)
  {
    var currentDocName = doc != null ? doc.Name : NULL_DOCUMENT_NAME;
    if (_previousDocName == currentDocName)
    {
      return;
    }

    _previousDocName = currentDocName;
    LoadState();
    await _eventAggregator.GetEvent<DocumentStoreChangedEvent>().PublishAsync(new object());
  }

  protected override void LoadState()
  {
    // POC: Will be addressed to move it into AutocadContext!
    Document? doc = Application.DocumentManager.MdiActiveDocument;

    if (doc == null)
    {
      ClearAndSave();
      return;
    }

    string? serializedModelCards = _autocadDocumentManager.ReadModelCards(doc);
    if (serializedModelCards == null)
    {
      ClearAndSave();
      return;
    }
    LoadFromString(serializedModelCards);
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    // POC: Will be addressed to move it into AutocadContext!
    Document doc = Application.DocumentManager.MdiActiveDocument;

    if (doc == null)
    {
      return;
    }

    _autocadDocumentManager.WriteModelCards(doc, modelCardState);
  }
}
