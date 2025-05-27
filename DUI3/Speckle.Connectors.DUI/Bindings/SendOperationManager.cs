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
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.DUI.Bindings;

[GenerateAutoInterface]
public class SendOperationManagerFactory(
  IServiceProvider serviceProvider,
  IOperationProgressManager operationProgressManager,
  DocumentModelStore store,
  ICancellationManager cancellationManager,
  ISpeckleApplication speckleApplication,
  ISdkActivityFactory activityFactory,
  ILoggerFactory loggerFactory
) : ISendOperationManagerFactory
{
  public ISendOperationManager Create() =>
    new SendOperationManager(
#pragma warning disable CA2000
      serviceProvider.CreateScope(),
#pragma warning restore CA2000
      operationProgressManager,
      store,
      cancellationManager,
      speckleApplication,
      activityFactory,
      loggerFactory.CreateLogger<SendOperationManager>()
    );
}

public partial interface ISendOperationManager : IDisposable;

[GenerateAutoInterface]
public sealed class SendOperationManager(
  IServiceScope serviceScope,
  IOperationProgressManager operationProgressManager,
  DocumentModelStore store,
  ICancellationManager cancellationManager,
  ISpeckleApplication speckleApplication,
  ISdkActivityFactory activityFactory,
  ILogger<SendOperationManager> logger
) : ISendOperationManager
{
  public async Task Process<T>(
    ISendBindingUICommands commands,
    string modelCardId,
    Action<IServiceProvider, SenderModelCard> initializeScope,
    Func<SenderModelCard, IReadOnlyList<T>> gatherObjects
  )
  {
    await Process(commands, modelCardId, initializeScope, (card, _) => Task.FromResult(gatherObjects(card)));
  }

  public async Task Process<T>(
    ISendBindingUICommands commands,
    string modelCardId,
    Action<IServiceProvider, SenderModelCard> initializeScope,
    Func<SenderModelCard, Task<IReadOnlyList<T>>> gatherObjects
  )
  {
    await Process(commands, modelCardId, initializeScope, async (card, _) => await gatherObjects(card));
  }

  public async Task Process<T>(
    ISendBindingUICommands commands,
    string modelCardId,
    Action<IServiceProvider, SenderModelCard> initializeScope,
    Func<SenderModelCard, IProgress<CardProgress>, Task<IReadOnlyList<T>>> gatherObjects
  )
  {
    using var activity = activityFactory.Start();
    try
    {
      if (store.GetModelById(modelCardId) is not SenderModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No publish model card was found.");
      }

      using var cancellationItem = cancellationManager.GetCancellationItem(modelCardId);

      initializeScope(serviceScope.ServiceProvider, modelCard);

      var progress = operationProgressManager.CreateOperationProgressEventHandler(commands.Bridge, modelCardId, cancellationItem.Token);

      var objects = await gatherObjects(modelCard, progress);

      if (objects.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendInfo = modelCard.GetSendInfo(speckleApplication.ApplicationAndVersion);

      var sendResult = await serviceScope
        .ServiceProvider.GetRequiredService<ISendOperation<T>>()
        .Execute(objects, sendInfo, progress, cancellationItem.Token);

      await commands.SetModelSendResult(modelCardId, sendResult.VersionId, sendResult.ConversionResults);
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
