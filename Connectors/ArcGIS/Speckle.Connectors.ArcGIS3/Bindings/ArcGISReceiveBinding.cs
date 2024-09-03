using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Sdk;

namespace Speckle.Connectors.ArcGIS.Bindings;

public sealed class ArcGISReceiveBinding : IReceiveBinding
{
  public string Name { get; } = "receiveBinding";
  private readonly CancellationManager _cancellationManager;
  private readonly DocumentModelStore _store;
  private readonly ILogger<ArcGISReceiveBinding> _logger;
  private readonly IArcGISSender _arcGisSender;

  private ReceiveBindingUICommands Commands { get; }
  public IBridge Parent { get; }

  public ArcGISReceiveBinding(
    DocumentModelStore store,
    IBridge parent,
    CancellationManager cancellationManager,
    ILogger<ArcGISReceiveBinding> logger,
    IArcGISSender arcGisSender
  )
  {
    _store = store;
    _cancellationManager = cancellationManager;
    Parent = parent;
    Commands = new ReceiveBindingUICommands(parent);
    _logger = logger;
    _arcGisSender = arcGisSender;
  }

  public async Task Receive(string modelCardId)
  {
    try
    {
      // Get receiver card
      if (_store.GetModelById(modelCardId) is not ReceiverModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No download model card was found.");
      }

      CancellationToken cancellationToken = _cancellationManager.InitCancellationTokenSource(modelCardId);

      // Receive host objects
      var receiveOperationResults = await _arcGisSender
        .ReceiveOperation(Parent, modelCard, cancellationToken)
        .ConfigureAwait(false);

      modelCard.BakedObjectIds = receiveOperationResults.BakedObjectIds.ToList();
      Commands.SetModelReceiveResult(
        modelCardId,
        receiveOperationResults.BakedObjectIds,
        receiveOperationResults.ConversionResults
      );
    }
    catch (OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
      return;
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      Commands.SetModelError(modelCardId, ex);
    }
  }

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
