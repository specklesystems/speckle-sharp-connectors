using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Logging;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public sealed class ReceiveOperation(
  IHostObjectBuilder hostObjectBuilder,
  IReceiveProgress receiveProgress,
  ISdkActivityFactory activityFactory,
  IOperations operations,
  IReceiveVersionRetriever receiveVersionRetriever,
  IThreadContext threadContext
) : IReceiveOperation
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
    var account = receiveInfo.Account;
    using var userScope = ActivityScope.SetTag(Consts.USER_ID, account.GetHashedEmail());
    var version = await receiveVersionRetriever.GetVersion(account, receiveInfo, cancellationToken);

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
    await receiveVersionRetriever.VersionReceived(account, version, receiveInfo, cancellationToken);
    return res;
  }

  public async Task<Base> ReceiveData(
    Account account,
    Speckle.Sdk.Api.GraphQL.Models.Version version,
    ReceiveInfo receiveInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    if (version.referencedObject is null)
    {
      throw new SpeckleException("Version referenced object is null and cannot do a receive operation.");
    }
    receiveProgress.Begin();
    Base commitObject = await operations.Receive2(
      new Uri(account.serverInfo.url),
      receiveInfo.ProjectId,
      version.referencedObject!,
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
    conversionActivity?.SetTag("receiveInfo.serverUrl", receiveInfo.Account.serverInfo.url);
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
    catch (OperationCanceledException)
    {
      //handle conversions but don't log to seq and also throw
      conversionActivity?.SetStatus(SdkActivityStatusCode.Error);
      throw;
    }
    catch (Exception ex)
    {
      conversionActivity?.RecordException(ex);
      conversionActivity?.SetStatus(SdkActivityStatusCode.Error);
      throw;
    }
  }
}
