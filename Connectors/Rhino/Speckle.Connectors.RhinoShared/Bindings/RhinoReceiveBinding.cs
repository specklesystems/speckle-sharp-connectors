using Microsoft.Extensions.Logging;
using Rhino;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;

namespace Speckle.Connectors.Rhino.Bindings;

public class RhinoReceiveBinding : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBridge Parent { get; }

  private readonly CancellationManager _cancellationManager;
  private readonly DocumentModelStore _store;
  private readonly IUnitOfWorkFactory _unitOfWorkFactory;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<RhinoReceiveBinding> _logger;
  private readonly IRhinoConversionSettingsFactory _rhinoConversionSettingsFactory;
  private ReceiveBindingUICommands Commands { get; }

  public RhinoReceiveBinding(
    DocumentModelStore store,
    CancellationManager cancellationManager,
    IBridge parent,
    IUnitOfWorkFactory unitOfWorkFactory,
    IOperationProgressManager operationProgressManager,
    ILogger<RhinoReceiveBinding> logger,
    IRhinoConversionSettingsFactory rhinoConversionSettingsFactory
  )
  {
    Parent = parent;
    _store = store;
    _unitOfWorkFactory = unitOfWorkFactory;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _rhinoConversionSettingsFactory = rhinoConversionSettingsFactory;
    _cancellationManager = cancellationManager;
    Commands = new ReceiveBindingUICommands(parent);
  }

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId)
  {
    using var unitOfWork = _unitOfWorkFactory.Create();
    unitOfWork
      .Resolve<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(_rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));
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
        .Resolve<ReceiveOperation>()
        .Execute(
          modelCard.GetReceiveInfo(Utils.Connector.Slug),
          cancellationToken,
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationToken)
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
      return;
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex).ConfigureAwait(false);
    }
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
