
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Core.Logging;

namespace Speckle.Connectors.ArcGIS.Bindings;

public sealed class ArcGISReceiveBinding : IReceiveBinding
{
  public string Name { get; } = "receiveBinding";
  private readonly CancellationManager _cancellationManager;
  private readonly DocumentModelStore _store;
  private readonly IUnitOfWorkFactory _unitOfWorkFactory;

  public ReceiveBindingUICommands Commands { get; }
  public IBridge Parent { get; }

  public ArcGISReceiveBinding(
    DocumentModelStore store,
    IBridge parent,
    CancellationManager cancellationManager,
    IUnitOfWorkFactory unitOfWorkFactory
  )
  {
    _store = store;
    _cancellationManager = cancellationManager;
    Parent = parent;
    Commands = new ReceiveBindingUICommands(parent);
    _unitOfWorkFactory = unitOfWorkFactory;
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

      // Init cancellation token source -> Manager also cancel it if exist before
      CancellationTokenSource cts = _cancellationManager.InitCancellationTokenSource(modelCardId);

      using IUnitOfWork<ReceiveOperation> unitOfWork = _unitOfWorkFactory.Resolve<ReceiveOperation>();

      // Receive host objects
      var receiveOperationResults = await unitOfWork
        .Service.Execute(
          modelCard.GetReceiveInfo("ArcGIS"), // POC: get host app name from settings? same for GetSendInfo
          cts.Token,
          (status, progress) =>
            Commands.SetModelProgress(modelCardId, new ModelCardProgress(modelCardId, status, progress), cts)
        )
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
    catch (Exception e) when (!e.IsFatal()) // UX reasons - we will report operation exceptions as model card error.
    {
      Commands.SetModelError(modelCardId, e);
    }
  }

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
