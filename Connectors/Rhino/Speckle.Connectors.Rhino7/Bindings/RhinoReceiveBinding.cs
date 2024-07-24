using Speckle.Autofac;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Rhino7.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;

namespace Speckle.Connectors.Rhino7.Bindings;

public class RhinoReceiveBinding : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBridge Parent { get; }

  private readonly CancellationManager _cancellationManager;
  private readonly DocumentModelStore _store;
  private readonly IUnitOfWorkFactory _unitOfWorkFactory;
  private readonly RhinoSettings _rhinoSettings;
  public ReceiveBindingUICommands Commands { get; }

  public RhinoReceiveBinding(
    DocumentModelStore store,
    CancellationManager cancellationManager,
    IBridge parent,
    IUnitOfWorkFactory unitOfWorkFactory,
    RhinoSettings rhinoSettings
  )
  {
    Parent = parent;
    _store = store;
    _unitOfWorkFactory = unitOfWorkFactory;
    _rhinoSettings = rhinoSettings;
    _cancellationManager = cancellationManager;
    Commands = new ReceiveBindingUICommands(parent);
  }

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId)
  {
    using var unitOfWork = _unitOfWorkFactory.Resolve<ReceiveOperation>();
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

      // Receive host objects
      HostObjectBuilderResult conversionResults = await unitOfWork
        .Service.Execute(
          modelCard.GetReceiveInfo(_rhinoSettings.HostAppInfo.Name),
          cts.Token,
          (status, progress) =>
            Commands.SetModelProgress(modelCardId, new ModelCardProgress(modelCardId, status, progress), cts)
        )
        .ConfigureAwait(false);

      modelCard.BakedObjectIds = conversionResults.BakedObjectIds.ToList();
      Commands.SetModelReceiveResult(
        modelCardId,
        conversionResults.BakedObjectIds,
        conversionResults.ConversionResults
      );
    }
    catch (Exception e) when (!e.IsFatal()) // UX reasons - we will report operation exceptions as model card error.
    {
      Commands.SetModelError(modelCardId, e);
    }
    catch (OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
      return;
    }
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
