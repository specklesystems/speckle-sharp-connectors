using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rhino;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;

namespace Speckle.Connectors.Rhino.Bindings;

public class RhinoReceiveBinding : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; }

  private readonly ICancellationManager _cancellationManager;
  private readonly DocumentModelStore _store;
  private readonly IServiceProvider _serviceProvider;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<RhinoReceiveBinding> _logger;
  private readonly IRhinoConversionSettingsFactory _rhinoConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private ReceiveBindingUICommands Commands { get; }

  public RhinoReceiveBinding(
    DocumentModelStore store,
    ICancellationManager cancellationManager,
    IBrowserBridge parent,
    IOperationProgressManager operationProgressManager,
    ILogger<RhinoReceiveBinding> logger,
    IRhinoConversionSettingsFactory rhinoConversionSettingsFactory,
    IServiceProvider serviceProvider,
    ISpeckleApplication speckleApplication
  )
  {
    Parent = parent;
    _store = store;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _rhinoConversionSettingsFactory = rhinoConversionSettingsFactory;
    _serviceProvider = serviceProvider;
    _speckleApplication = speckleApplication;
    _cancellationManager = cancellationManager;
    Commands = new ReceiveBindingUICommands(parent);
  }

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId)
  {
    using var scope = _serviceProvider.CreateScope();
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(_rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    uint undoRecord = 0;
    try
    {
      // Get receiver card
      if (_store.GetModelById(modelCardId) is not ReceiverModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No download model card was found.");
      }

      using var cancellationItem = _cancellationManager.GetCancellationItem(modelCardId);

      undoRecord = RhinoDoc.ActiveDoc.BeginUndoRecord($"Receive Speckle model {modelCard.ModelName}");
      // Receive host objects
      HostObjectBuilderResult conversionResults = await scope
        .ServiceProvider.GetRequiredService<IReceiveOperation>()
        .Execute(
          modelCard.GetReceiveInfo(_speckleApplication.Slug),
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationItem.Token),
          cancellationItem.Token
        );

      modelCard.BakedObjectIds = conversionResults.BakedObjectIds.ToList();
      await Commands.SetModelReceiveResult(
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
      await Commands.SetModelError(modelCardId, ex);
    }
    finally
    {
      RhinoDoc.ActiveDoc.EndUndoRecord(undoRecord);
    }
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
