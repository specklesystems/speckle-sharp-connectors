using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;

namespace Speckle.Connectors.DUI.Bindings;


[GenerateAutoInterface]
public class SendOperationManagerFactory(IServiceProvider serviceProvider, 
  IOperationProgressManager operationProgressManager,
  DocumentModelStore store,
  ICancellationManager cancellationManager,
  ISpeckleApplication speckleApplication,
  ILoggerFactory loggerFactory)
  : ISendOperationManagerFactory
{
  public ISendOperationManager Create() =>
    new SendOperationManager(
#pragma warning disable CA2000
      serviceProvider.CreateScope(),
#pragma warning restore CA2000
      operationProgressManager,
      store,
      cancellationManager, speckleApplication,
      loggerFactory.CreateLogger<SendOperationManager>()
    );
}

public partial interface ISendOperationManager : IDisposable
{
  T GetScoped<T>();
}

[GenerateAutoInterface]
public sealed class SendOperationManager(IServiceScope serviceScope, 
  IOperationProgressManager operationProgressManager,
   DocumentModelStore store,
   ICancellationManager cancellationManager,
  ISpeckleApplication speckleApplication,
  ILogger<SendOperationManager> logger)
  : ISendOperationManager
{
  public async Task Send<T>(IReadOnlyList<T> objects,
    SendBindingUICommands   commands, 
    SendInfo sendInfo,
    string modelCardId,
    CancellationToken cancellationToken)
  {
    var sendResult = await serviceScope
      .ServiceProvider.GetRequiredService<SendOperation<T>>()
      .Execute(
        objects,
        sendInfo,
        operationProgressManager.CreateOperationProgressEventHandler(commands.Bridge, modelCardId, cancellationToken),
        cancellationToken
      );

    await commands.SetModelSendResult(modelCardId, sendResult.VersionId, sendResult.ConversionResults);
  }
  
  [AutoInterfaceIgnore]
  public T GetScoped<T>()
   =>
    serviceScope.ServiceProvider.GetRequiredService<T>();

  public async Task Process<T>(
    
    SendBindingUICommands   commands, 
    string modelCardId,
    Func<SenderModelCard, IReadOnlyList<T>> gatherObjects)
  {
    try
    {
      if (store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No publish model card was found.");
      }

      using var cancellationItem = cancellationManager.GetCancellationItem(modelCardId);

      var objects = gatherObjects(modelCard);

      if (objects.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendInfo = modelCard.GetSendInfo(speckleApplication.ApplicationAndVersion); 
      await Send(objects, commands, sendInfo, modelCardId, 
        cancellationItem.Token
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
      logger.LogModelCardHandledError(ex);
      await commands.SetModelError(modelCardId, ex);
    }
  }

  [AutoInterfaceIgnore]
  public void Dispose() => serviceScope.Dispose();
}
