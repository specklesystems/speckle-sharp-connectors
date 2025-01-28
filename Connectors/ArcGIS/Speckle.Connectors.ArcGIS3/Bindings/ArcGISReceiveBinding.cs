using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Sdk;

namespace Speckle.Connectors.ArcGIS.Bindings;

public sealed class ArcGISReceiveBinding : IReceiveBinding
{
  public string Name { get; } = "receiveBinding";
  private readonly CancellationManager _cancellationManager;
  private readonly DocumentModelStore _store;
  private readonly IServiceProvider _serviceProvider;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<ArcGISReceiveBinding> _logger;
  private readonly IArcGISConversionSettingsFactory _arcGISConversionSettingsFactory;

  private ReceiveBindingUICommands Commands { get; }
  public IBrowserBridge Parent { get; }

  public ArcGISReceiveBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    CancellationManager cancellationManager,
    IServiceProvider serviceProvider,
    IOperationProgressManager operationProgressManager,
    ILogger<ArcGISReceiveBinding> logger,
    IArcGISConversionSettingsFactory arcGisConversionSettingsFactory
  )
  {
    _store = store;
    _cancellationManager = cancellationManager;
    Parent = parent;
    Commands = new ReceiveBindingUICommands(parent);
    _serviceProvider = serviceProvider;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _arcGISConversionSettingsFactory = arcGisConversionSettingsFactory;
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

      using var cancellationItem = _cancellationManager.GetCancellationItem(modelCardId);
      using var scope = _serviceProvider.CreateScope();
      scope
        .ServiceProvider.GetRequiredService<IConverterSettingsStore<ArcGISConversionSettings>>()
        .Initialize(
          _arcGISConversionSettingsFactory.Create(
            Project.Current,
            MapView.Active.Map,
            new CRSoffsetRotation(MapView.Active.Map)
          )
        );
      // Receive host objects
      var receiveOperationResults = await scope
        .ServiceProvider.GetRequiredService<ReceiveOperation>()
        .Execute(
          modelCard.GetReceiveInfo("ArcGIS"), // POC: get host app name from settings? same for GetSendInfo
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationItem.Token),
          cancellationItem.Token
        );

      modelCard.BakedObjectIds = receiveOperationResults.BakedObjectIds.ToList();
      await Commands.SetModelReceiveResult(
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
      await Commands.SetModelError(modelCardId, ex);
    }
  }

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
