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
    uint docSerialNumber = RhinoDoc.ActiveDoc.RuntimeSerialNumber;

    void Write()
    {
      // the doc may have closed while the write was queued
      var doc = RhinoDoc.FromRuntimeSerialNumber(docSerialNumber);
      if (doc == null)
      {
        return;
      }
      doc.Strings.Delete(SPECKLE_KEY);
      doc.Strings.SetString(SPECKLE_KEY, SPECKLE_KEY, modelCardState);
    }

    // Bindings run on a worker thread, and on macOS writing a doc string marks the NSDocument edited, which AppKit
    // only allows on the main thread (fatal NSInternalInconsistencyException otherwise). FEA-124.
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
    string stateString = RhinoDoc.ActiveDoc.Strings.GetValue(SPECKLE_KEY, SPECKLE_KEY);
    LoadFromString(stateString);
  }
}
