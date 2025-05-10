using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.Bindings;

internal sealed class RevitReceiveBinding : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; }

  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<RevitReceiveBinding> _logger;
  private readonly ICancellationManager _cancellationManager;
  private readonly DocumentModelStore _store;
  private readonly IServiceProvider _serviceProvider;
  private readonly IRevitConversionSettingsFactory _revitConversionSettingsFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;
  private ReceiveBindingUICommands Commands { get; }

  public RevitReceiveBinding(
    DocumentModelStore store,
    ICancellationManager cancellationManager,
    IBrowserBridge parent,
    IServiceProvider serviceProvider,
    IOperationProgressManager operationProgressManager,
    ILogger<RevitReceiveBinding> logger,
    IRevitConversionSettingsFactory revitConversionSettingsFactory,
    ISpeckleApplication speckleApplication,
    RevitToHostCacheSingleton revitToHostCacheSingleton
  )
  {
    Parent = parent;
    _store = store;
    _serviceProvider = serviceProvider;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _revitConversionSettingsFactory = revitConversionSettingsFactory;
    _speckleApplication = speckleApplication;
    _cancellationManager = cancellationManager;
    _revitToHostCacheSingleton = revitToHostCacheSingleton;

    Commands = new ReceiveBindingUICommands(parent);
  }

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

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
        .ServiceProvider.GetRequiredService<IConverterSettingsStore<RevitConversionSettings>>()
        .Initialize(
          _revitConversionSettingsFactory.Create(
            DetailLevelType.Coarse, // TODO figure out
            null,
            false,
            true,
            false
          )
        );
      // Receive host objects
      HostObjectBuilderResult conversionResults = await scope
        .ServiceProvider.GetRequiredService<ReceiveOperation>()
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
    }
    catch (SpeckleRevitTaskException ex)
    {
      await SpeckleRevitTaskException.ProcessException(modelCardId, ex, _logger, Commands);
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex);
    }
    finally
    {
      // otherwise the id of the operation persists on the cancellation manager and triggers 'Operations cancelled because of document swap!' message to UI.
      _cancellationManager.CancelOperation(modelCardId);
    }
  }
}
