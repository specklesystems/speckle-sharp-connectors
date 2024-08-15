using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Sdk;

namespace Speckle.Connectors.Autocad.Bindings;

public sealed class AutocadReceiveBinding : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly CancellationManager _cancellationManager;
  private readonly IUnitOfWorkFactory _unitOfWorkFactory;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<AutocadReceiveBinding> _logger;

  private ReceiveBindingUICommands Commands { get; }

  public AutocadReceiveBinding(
    DocumentModelStore store,
    IBridge parent,
    CancellationManager cancellationManager,
    IUnitOfWorkFactory unitOfWorkFactory,
    IOperationProgressManager operationProgressManager,
    ILogger<AutocadReceiveBinding> logger
  )
  {
    _store = store;
    _cancellationManager = cancellationManager;
    _unitOfWorkFactory = unitOfWorkFactory;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    Parent = parent;
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

      CancellationToken cancellationToken = _cancellationManager.InitCancellationTokenSource(modelCardId);

      // Disable document activation (document creation and document switch)
      // Not disabling results in DUI model card being out of sync with the active document
      // The DocumentActivated event isn't usable probably because it is pushed to back of main thread queue
      Application.DocumentManager.DocumentActivationEnabled = false;

      // Receive host objects
      var operationResults = await unitOfWork
        .Service.Execute(
          modelCard.GetReceiveInfo(Speckle.Connectors.Utils.Connector.Slug),
          cancellationToken,
          (status, progress) =>
            _operationProgressManager.SetModelProgress(
              Parent,
              modelCardId,
              new ModelCardProgress(modelCardId, status, progress),
              cancellationToken
            )
        )
        .ConfigureAwait(false);

      Commands.SetModelReceiveResult(modelCardId, operationResults.BakedObjectIds, operationResults.ConversionResults);
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
    finally
    {
      // reenable document activation
      Application.DocumentManager.DocumentActivationEnabled = true;
    }
  }
}
