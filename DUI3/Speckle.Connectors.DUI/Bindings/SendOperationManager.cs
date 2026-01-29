using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.DUI.Bindings;

public partial interface ISendOperationManager : IDisposable;

[GenerateAutoInterface]
public sealed class SendOperationManager(
  IServiceScope serviceScope,
  IOperationProgressManager operationProgressManager,
  DocumentModelStore store,
  ICancellationManager cancellationManager,
  ISdkActivityFactory activityFactory,
  IClientFactory clientFactory,
  IAccountManager accountManager,
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
      using SendInfo sendInfo = GetSendInfo(modelCard);
      using var userScope = UserActivityScope.AddUserScope(sendInfo.Account);

      using var cancellationItem = cancellationManager.GetCancellationItem(modelCardId);

      initializeScope(serviceScope.ServiceProvider, modelCard);

      var progress = operationProgressManager.CreateOperationProgressEventHandler(
        commands.Bridge,
        modelCardId,
        cancellationItem.Token
      );

      var objects = await gatherObjects(modelCard, progress);

      if (objects.Count == 0)
      {
        // Handle as CARD ERROR in this function
        throw new SpeckleSendFilterException("No objects were found to convert. Please update your publish filter!");
      }

      var sendOperation = serviceScope.ServiceProvider.GetRequiredService<ISendOperation<T>>();

      SendOperationResult result;
      string versionId;
      bool useModelIngestionSend = await CheckUseModelIngestionSend(sendInfo);

      if (useModelIngestionSend)
      {
        (result, versionId) = await sendOperation.SendViaIngestion(
          objects,
          sendInfo,
          null,
          null,
          null,
          progress,
          cancellationItem.Token
        );
      }
      else
      {
        (result, versionId) = await sendOperation.SendViaVersionCreate(
          objects,
          sendInfo,
          null,
          progress,
          cancellationItem.Token
        );
      }

      await commands.SetModelSendResult(modelCardId, versionId, result.ConversionResults);
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

  /// <summary>
  /// There are three paths for this function:
  /// <ul>
  /// <li>Server Supports ingestion, and the user has permission to create an ingetsion => returns <see langword="true"/></li>
  /// <li> Server doesn't support ingestions (i.e. public server or old servers) => returns <see langword="false"/></li>
  /// <li> Server Supports ingestions, but the user doesn't have permission to create ingestion => throws <see cref="WorkspacePermissionException"/></li>
  /// </ul>
  /// </summary>
  /// <param name="sendInfo"></param>
  /// <returns><see langword="true"/> if we should use model ingestion based send functions, false</returns>
  /// <exception cref="WorkspacePermissionException">Thrown if the server supports model ingestion, but for other reasons we won't beable to create an ingestion</exception>
  private static async Task<bool> CheckUseModelIngestionSend(SendInfo sendInfo)
  {
    bool useModelIngestionSend = true;
    try
    {
      PermissionCheckResult permissionCheck = await sendInfo.Client.Model.CanCreateModelIngestion(
        sendInfo.ProjectId,
        sendInfo.ModelId
      );
      permissionCheck.EnsureAuthorised();
    }
    catch (SpeckleGraphQLInvalidQueryException)
    {
      // CanCreateModelIngestion will throw this if the server is too old and doesn't support model ingestion API
      useModelIngestionSend = false;
    }

    return useModelIngestionSend;
  }

  private SendInfo GetSendInfo(SenderModelCard modelCard)
  {
    var account = accountManager.GetAccount(modelCard.AccountId.NotNull());
    var client = clientFactory.Create(account);
    return new(client, modelCard.ProjectId.NotNull(), modelCard.ModelId.NotNull());
  }

  [AutoInterfaceIgnore]
  public void Dispose() => serviceScope.Dispose();
}
