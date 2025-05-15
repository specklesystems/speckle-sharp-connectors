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
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    if (RhinoDoc.ActiveDoc == null)
    {
      return; // Should throw
    }
    RhinoDoc.ActiveDoc.Strings.Delete(SPECKLE_KEY);
    RhinoDoc.ActiveDoc.Strings.SetString(SPECKLE_KEY, SPECKLE_KEY, modelCardState);
  }

  protected override void LoadState()
  {
    string stateString = RhinoDoc.ActiveDoc.Strings.GetValue(SPECKLE_KEY, SPECKLE_KEY);
    LoadFromString(stateString);
  }
}
