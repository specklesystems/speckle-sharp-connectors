using Autofac;
using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.Bindings;

internal sealed class RevitReceiveBinding : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBridge Parent { get; }

  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<RevitReceiveBinding> _logger;
  private readonly CancellationManager _cancellationManager;
  private readonly DocumentModelStore _store;
  private readonly IUnitOfWorkFactory _unitOfWorkFactory;
  private ReceiveBindingUICommands Commands { get; }

  public RevitReceiveBinding(
    DocumentModelStore store,
    CancellationManager cancellationManager,
    IBridge parent,
    IUnitOfWorkFactory unitOfWorkFactory,
    IOperationProgressManager operationProgressManager,
    ILogger<RevitReceiveBinding> logger
  )
  {
    Parent = parent;
    _store = store;
    _unitOfWorkFactory = unitOfWorkFactory;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _cancellationManager = cancellationManager;

    Commands = new ReceiveBindingUICommands(parent);
  }

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId)
  {
    using var unitOfWork = _unitOfWorkFactory.Resolve<ReceiveOperation>(b =>
    {
      b.RegisterType<ToSpeckleSettings>().SingleInstance();
      b.Register(c => new ToSpeckleSettings(default, null));
    });

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
      HostObjectBuilderResult conversionResults = await unitOfWork
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

      modelCard.BakedObjectIds = conversionResults.BakedObjectIds.ToList();
      Commands.SetModelReceiveResult(
        modelCardId,
        conversionResults.BakedObjectIds,
        conversionResults.ConversionResults
      );
    }
    catch (OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      Commands.SetModelError(modelCardId, ex);
    }
  }
}
