using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Logging;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;
using Speckle.Sdk.Serialisation.V2.Send;

namespace Speckle.Connectors.Common.Operations;

public sealed class SendOperation<T>(
  IRootObjectBuilder<T> rootObjectBuilder,
  ISendConversionCache sendConversionCache,
  AccountService accountService,
  ISendProgress sendProgress,
  IOperations operations,
  IClientFactory clientFactory,
  ISdkActivityFactory activityFactory,
  IThreadContext threadContext
)
{
  public async Task<SendOperationResult> Execute(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var buildResult = await threadContext.RunOnMainAsync(
      async () => await rootObjectBuilder.BuildAsync(objects, sendInfo, onOperationProgressed, ct)
    );

    // POC: Jonathon asks on behalf of willow twin - let's explore how this can work
    // buildResult.RootObject["@report"] = new Report { ConversionResults = buildResult.ConversionResults };

    buildResult.RootObject["version"] = 3;
    // base object handler is separated, so we can do some testing on non-production databases
    // exact interface may want to be tweaked when we implement this
    var (rootObjId, convertedReferences) = await threadContext.RunOnWorkerAsync(
      () => Send(buildResult.RootObject, sendInfo, onOperationProgressed, ct)
    );

    return new(rootObjId, convertedReferences, buildResult.ConversionResults);
  }

  private async Task<SerializeProcessResults> Send(
    Base commitObject,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Uploading...", null));

    Account account = accountService.GetAccountWithServerUrlFallback(sendInfo.AccountId, sendInfo.ServerUrl);
    using var userScope = ActivityScope.SetTag(Consts.USER_ID, account.GetHashedEmail());
    using var activity = activityFactory.Start("SendOperation");

    sendProgress.Begin();
    var sendResult = await operations.Send2(
      sendInfo.ServerUrl,
      sendInfo.ProjectId,
      account.token,
      commitObject,
      onProgressAction: new PassthroughProgress(args => sendProgress.Report(onOperationProgressed, args)),
      ct
    );

    sendConversionCache.StoreSendResult(sendInfo.ProjectId, sendResult.ConvertedReferences);

    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Linking version to model...", null));

    // 8 - Create the version (commit)
    using var apiClient = clientFactory.Create(account);
    _ = await apiClient
      .Version.Create(
        new CreateVersionInput(
          sendResult.RootId,
          sendInfo.ModelId,
          sendInfo.ProjectId,
          sourceApplication: sendInfo.SourceApplication
        ),
        ct
      )
      .ConfigureAwait(true);

    return sendResult;
  }
}

public record SendOperationResult(
  string RootObjId,
  IReadOnlyDictionary<Id, ObjectReference> ConvertedReferences,
  IEnumerable<SendConversionResult> ConversionResults
);
