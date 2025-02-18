using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Logging;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Connectors.Common.Operations;

public sealed class ReceiveOperation(
  IHostObjectBuilder hostObjectBuilder,
  AccountService accountService,
  IReceiveProgress receiveProgress,
  ISdkActivityFactory activityFactory,
  IOperations operations,
  IClientFactory clientFactory,
  IThreadContext threadContext
)
{
  public async Task<HostObjectBuilderResult> Execute(
    ReceiveInfo receiveInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var execute = activityFactory.Start("Receive Operation");
    cancellationToken.ThrowIfCancellationRequested();
    execute?.SetTag("receiveInfo", receiveInfo);
    // 2 - Check account exist
    Account account = accountService.GetAccountWithServerUrlFallback(receiveInfo.AccountId, receiveInfo.ServerUrl);
    using Client apiClient = clientFactory.Create(account);
    using var userScope = ActivityScope.SetTag(Consts.USER_ID, account.GetHashedEmail());

    var version = await apiClient.Version.Get(receiveInfo.SelectedVersionId, receiveInfo.ProjectId, cancellationToken);

    cancellationToken.ThrowIfCancellationRequested();
    var commitObject = await threadContext.RunOnWorkerAsync(
      () => ReceiveData(account, version, receiveInfo, onOperationProgressed, cancellationToken)
    );

    // 4 - Convert objects
    HostObjectBuilderResult res = await ConvertObjects(
        commitObject,
        receiveInfo,
        onOperationProgressed,
        cancellationToken
      )
      .ConfigureAwait(false);

    cancellationToken.ThrowIfCancellationRequested();
    await apiClient.Version.Received(
      new(version.id, receiveInfo.ProjectId, receiveInfo.SourceApplication),
      cancellationToken
    );

    return res;
  }

  private async Task<Base> ReceiveData(
    Account account,
    Speckle.Sdk.Api.GraphQL.Models.Version version,
    ReceiveInfo receiveInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    receiveProgress.Begin();
    Base commitObject = await operations.Receive2(
      new Uri(account.serverInfo.url),
      receiveInfo.ProjectId,
      version.referencedObject,
      account.token,
      onProgressAction: new PassthroughProgress(args => receiveProgress.Report(onOperationProgressed, args)),
      cancellationToken: cancellationToken
    );

    cancellationToken.ThrowIfCancellationRequested();
    return commitObject;
  }

  private async Task<HostObjectBuilderResult> ConvertObjects(
    Base commitObject,
    ReceiveInfo receiveInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var conversionActivity = activityFactory.Start("ReceiveOperation.ConvertObjects");
    conversionActivity?.SetTag("smellsLikeV2Data", commitObject.SmellsLikeV2Data());
    conversionActivity?.SetTag("receiveInfo.serverUrl", receiveInfo.ServerUrl);
    conversionActivity?.SetTag("receiveInfo.projectId", receiveInfo.ProjectId);
    conversionActivity?.SetTag("receiveInfo.modelId", receiveInfo.ModelId);
    conversionActivity?.SetTag("receiveInfo.selectedVersionId", receiveInfo.SelectedVersionId);
    conversionActivity?.SetTag("receiveInfo.sourceApplication", receiveInfo.SourceApplication);

    try
    {
      HostObjectBuilderResult res = await hostObjectBuilder
        .Build(commitObject, receiveInfo.ProjectName, receiveInfo.ModelName, onOperationProgressed, cancellationToken)
        .ConfigureAwait(false);
      conversionActivity?.SetStatus(SdkActivityStatusCode.Ok);
      return res;
    }
    catch (Exception ex)
    {
      conversionActivity?.RecordException(ex);
      conversionActivity?.SetStatus(SdkActivityStatusCode.Error);
      throw;
    }
  }
}
