using Speckle.Autofac;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Core.Common;

namespace Speckle.Connectors.Revit.Bindings;

internal sealed class RevitReceiveBinding : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBridge Parent { get; }

  private readonly RevitSettings _revitSettings;
  private readonly CancellationManager _cancellationManager;
  private readonly DocumentModelStore _store;
  private readonly IUnitOfWorkFactory _unitOfWorkFactory;
  private ReceiveBindingUICommands Commands { get; }

  private OperationProgressManager OperationProgressManager { get; }

  public RevitReceiveBinding(
    DocumentModelStore store,
    CancellationManager cancellationManager,
    IBridge parent,
    IUnitOfWorkFactory unitOfWorkFactory,
    RevitSettings revitSettings
  )
  {
    Parent = parent;
    _store = store;
    _unitOfWorkFactory = unitOfWorkFactory;
    _revitSettings = revitSettings;
    _cancellationManager = cancellationManager;
    Commands = new ReceiveBindingUICommands(parent);
    OperationProgressManager = new OperationProgressManager(parent);
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
          modelCard.GetReceiveInfo(_revitSettings.HostSlug.NotNull()),
          cts.Token,
          (status, progress) =>
            OperationProgressManager.SetModelProgress(
              modelCardId,
              new ModelCardProgress(modelCardId, status, progress),
              cts
            )
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
}
