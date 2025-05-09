using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.Autocad.HostApp;

public class AutocadDocumentStore : DocumentModelStore
{
  private const string NULL_DOCUMENT_NAME = "Null Doc";
  private string _previousDocName;
  private readonly AutocadDocumentManager _autocadDocumentManager;

  public AutocadDocumentStore(
    ILogger<DocumentModelStore> logger,
    IJsonSerializer jsonSerializer,
    AutocadDocumentManager autocadDocumentManager,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base(logger, jsonSerializer)
  {
    _autocadDocumentManager = autocadDocumentManager;
    _previousDocName = NULL_DOCUMENT_NAME;

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
    var currentDocName = doc != null ? doc.Name : NULL_DOCUMENT_NAME;
    if (_previousDocName == currentDocName)
    {
      return;
    }

    _previousDocName = currentDocName;
    LoadState();
    OnDocumentChanged();
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
