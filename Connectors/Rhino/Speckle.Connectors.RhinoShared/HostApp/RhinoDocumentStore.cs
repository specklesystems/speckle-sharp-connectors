using Rhino;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.RhinoShared;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoDocumentStore : DocumentModelStore
{
  private readonly IEventAggregator _eventAggregator;
  private const string SPECKLE_KEY = "Speckle_DUI3";
  public override bool IsDocumentInit { get; set; } = true; // Note: because of rhino implementation details regarding expiry checking of sender cards.

  public RhinoDocumentStore(IJsonSerializer jsonSerializer, IEventAggregator eventAggregator)
    : base(jsonSerializer)
  {
    _eventAggregator = eventAggregator;
    eventAggregator.GetEvent<BeginOpenDocument>().Subscribe(OnBeginOpenDocument);
    eventAggregator.GetEvent<EndOpenDocument>().Subscribe(OnEndOpenDocument);
  }

  private void OnBeginOpenDocument(object _) => IsDocumentInit = false;

  private async Task OnEndOpenDocument(DocumentOpenEventArgs e)
  {
    if (e.Merge)
    {
      return;
    }

    if (e.Document == null)
    {
      return;
    }

    IsDocumentInit = true;
    LoadState();
    await _eventAggregator.GetEvent<DocumentStoreChangedEvent>().PublishAsync(new object());
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
