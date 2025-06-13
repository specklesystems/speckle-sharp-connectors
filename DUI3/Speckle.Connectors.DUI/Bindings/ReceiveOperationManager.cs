using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;

namespace Speckle.Connectors.DUI.Bindings;

public partial interface IReceiveOperationManager : IDisposable;

[GenerateAutoInterface]
public sealed class ReceiveOperationManager(
  IServiceScope serviceScope,
  ICancellationManager cancellationManager,
  IDocumentModelStore store,
  ISpeckleApplication speckleApplication,
  IOperationProgressManager operationProgressManager,
  IAccountService accountService,
  ILogger<ReceiveOperationManager> logger
) : IReceiveOperationManager
{
  public async Task Process(
    IReceiveBindingUICommands commands,
    string modelCardId,
    Action<IServiceProvider> initializeScope,
    Func<string?, Func<Task<HostObjectBuilderResult>>, Task<HostObjectBuilderResult?>> processor
  )
  {
    // Get receiver card
    if (store.GetModelById(modelCardId) is not ReceiverModelCard modelCard)
    {
      // Handle as GLOBAL ERROR at BrowserBridge
      throw new InvalidOperationException("No download model card was found.");
    }
    try
    {
      using var cancellationItem = cancellationManager.GetCancellationItem(modelCardId);

      initializeScope(serviceScope.ServiceProvider);
      var progress = operationProgressManager.CreateOperationProgressEventHandler(
        commands.Bridge,
        modelCardId,
        cancellationItem.Token
      );
      var ro = serviceScope.ServiceProvider.GetRequiredService<IReceiveOperation>();
      var conversionResults = await processor(
        modelCard.ModelName,
        () =>
          ro.Execute(
            modelCard.GetReceiveInfo(accountService, speckleApplication.Slug),
            progress,
            cancellationItem.Token
          )
      );

      if (conversionResults is null)
      {
        return;
      }

      modelCard.BakedObjectIds = conversionResults.BakedObjectIds.ToList();
      await commands.SetModelReceiveResult(
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
      logger.LogModelCardHandledError(ex);
      await commands.SetModelError(modelCardId, ex);
    }
    finally
    {
      // otherwise the id of the operation persists on the cancellation manager and triggers 'Operations cancelled because of document swap!' message to UI.
      cancellationManager.CancelOperation(modelCardId);
    }
  }

  [AutoInterfaceIgnore]
  public void Dispose() => serviceScope.Dispose();
}
