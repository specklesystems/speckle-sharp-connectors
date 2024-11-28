using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Threading;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;
#pragma warning disable CS9113 // Parameter is unread.

namespace Speckle.Connectors.Common.Operations;

public sealed class ReceiveOperation(
  IHostObjectBuilder hostObjectBuilder,
  AccountService accountService,
  IReceiveProgress receiveProgress,
  ISdkActivityFactory activityFactory,
  IOperations operations,
  IClientFactory clientFactory,
  IThreadContext threadContext,
  IThreadOptions threadOptions
)
{
  public Task<HostObjectBuilderResult> Execute(
    ReceiveInfo receiveInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    throw new NotImplementedException("This is a placeholder for now.");
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
    Base? commitObject = await operations
      .Receive2(
        new Uri(account.serverInfo.url),
        receiveInfo.ProjectId,
        version.referencedObject,
        account.token,
        onProgressAction: new PassthroughProgress(args => receiveProgress.Report(onOperationProgressed, args)),
        cancellationToken: cancellationToken
      )
      .BackToAny();

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
        .BackToAny();
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
