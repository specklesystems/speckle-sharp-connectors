using Microsoft.Extensions.Logging;
using Rhino;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoDocumentStore : DocumentModelStore
{
  private const string SPECKLE_KEY = "Speckle_DUI3";
  public override bool IsDocumentInit { get; set; } = true; // Note: because of rhino implementation details regarding expiry checking of sender cards.
#if RHINO_MAC
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  /// <summary>The document whose model cards are in memory; saves go back to it, not to whichever is active now.</summary>
  private uint _loadedDocumentSerialNumber;
#endif

  public RhinoDocumentStore(
    ILogger<DocumentModelStore> logger,
    IJsonSerializer jsonSerializer,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base(logger, jsonSerializer)
  {
    RhinoDoc.BeginOpenDocument += (_, _) => topLevelExceptionHandler.CatchUnhandled(() => IsDocumentInit = false);
    RhinoDoc.EndOpenDocument += (_, e) =>
      topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (e.Document == null)
        {
          return;
        }

        IsDocumentInit = true;
        LoadState();
        OnDocumentChanged();
      });
#if RHINO_MAC
    _topLevelExceptionHandler = topLevelExceptionHandler;
    // Mac Rhino keeps several documents open in one process and switches between them without reopening, so the
    // cards in memory must follow the active document. FEA-124.
    RhinoDoc.ActiveDocumentChanged += (_, e) =>
      topLevelExceptionHandler.CatchUnhandled(() =>
      {
        if (e.Document == null || e.Document.RuntimeSerialNumber == _loadedDocumentSerialNumber)
        {
          return;
        }

        LoadState();
        OnDocumentChanged();
      });
    // Rhino Mac loads the plug-in on first use, after a double-clicked file has already opened, so EndOpenDocument
    // above never fires for it and the file's model cards would stay unread. FEA-124.
    if (RhinoDoc.ActiveDoc != null)
    {
      LoadState();
    }
#endif
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    if (RhinoDoc.ActiveDoc == null)
    {
      return; // Should throw
    }
#if RHINO_MAC
    uint documentSerialNumber =
      _loadedDocumentSerialNumber != 0 ? _loadedDocumentSerialNumber : RhinoDoc.ActiveDoc.RuntimeSerialNumber;

    void Write() =>
      _topLevelExceptionHandler.CatchUnhandled(() =>
      {
        var doc = RhinoDoc.FromRuntimeSerialNumber(documentSerialNumber);
        if (doc == null)
        {
          return;
        }
        doc.Strings.Delete(SPECKLE_KEY);
        doc.Strings.SetString(SPECKLE_KEY, SPECKLE_KEY, modelCardState);
      });

    // Writing a doc string marks the NSDocument edited, which AppKit only allows on the main thread. Queued rather
    // than waited on (unlike MacMainThread.Run): SaveState holds the model lock, and a main-thread caller waiting on
    // that lock would deadlock. FEA-124.
    if (RhinoApp.InvokeRequired)
    {
      RhinoApp.InvokeOnUiThread(Write);
    }
    else
    {
      Write();
    }
#else
    RhinoDoc.ActiveDoc.Strings.Delete(SPECKLE_KEY);
    RhinoDoc.ActiveDoc.Strings.SetString(SPECKLE_KEY, SPECKLE_KEY, modelCardState);
#endif
  }

  protected override void LoadState()
  {
#if RHINO_MAC
    _loadedDocumentSerialNumber = RhinoDoc.ActiveDoc.RuntimeSerialNumber;
#endif
    string stateString = RhinoDoc.ActiveDoc.Strings.GetValue(SPECKLE_KEY, SPECKLE_KEY);
    LoadFromString(stateString);
  }
}
