using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.Autocad.HostApp;

public class AutocadDocumentStore : DocumentModelStore
{
  private readonly string _nullDocumentName = "Null Doc";
  private string _previousDocName;
  private readonly AutocadDocumentManager _autocadDocumentManager;
  private readonly IEventAggregator _eventAggregator;

  public AutocadDocumentStore(
    IJsonSerializer jsonSerializer,
    AutocadDocumentManager autocadDocumentManager,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IEventAggregator eventAggregator
  )
    : base(jsonSerializer)
  {
    _autocadDocumentManager = autocadDocumentManager;
    _eventAggregator = eventAggregator;
    _previousDocName = _nullDocumentName;

    // POC: Will be addressed to move it into AutocadContext!
    if (Application.DocumentManager.MdiActiveDocument != null)
    {
      IsDocumentInit = true;
      // POC: this logic might go when we have document management in context
      // It is with the case of if binding created with already a document
      // This is valid when user opens acad file directly double clicking
      OnDocChangeInternal(Application.DocumentManager.MdiActiveDocument);
    }

    Application.DocumentManager.DocumentActivated += (_, e) =>
      topLevelExceptionHandler.CatchUnhandled(() => OnDocChangeInternal(e.Document));

    // since below event triggered as secondary, it breaks the logic in OnDocChangeInternal function, leaving it here for now.
    // Autodesk.AutoCAD.ApplicationServices.Application.DocumentWindowCollection.DocumentWindowActivated += (_, args) =>
    //  OnDocChangeInternal((Document)args.DocumentWindow.Document);
  }

  private void OnDocChangeInternal(Document? doc)
  {
    var currentDocName = doc != null ? doc.Name : _nullDocumentName;
    if (_previousDocName == currentDocName)
    {
      return;
    }

    _previousDocName = currentDocName;
    LoadState();
    _eventAggregator.GetEvent<DocumentStoreChangedEvent>().Publish(new object());
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
