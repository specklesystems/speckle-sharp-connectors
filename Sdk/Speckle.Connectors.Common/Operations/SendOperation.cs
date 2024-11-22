using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Logging;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Common.Operations;

public sealed class SendOperation<T>(
  IRootObjectBuilder<T> rootObjectBuilder,
  ISendConversionCache sendConversionCache,
  AccountService accountService,
  ISendProgress sendProgress,
  IOperations operations,
  IClientFactory clientFactory,
  ISdkActivityFactory activityFactory)
{
  public async Task<SendOperationResult> Execute(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var buildResult = await rootObjectBuilder
      .Build(objects, sendInfo, onOperationProgressed, ct)
      .ConfigureAwait(false);

    // POC: Jonathon asks on behalf of willow twin - let's explore how this can work
    // buildResult.RootObject["@report"] = new Report { ConversionResults = buildResult.ConversionResults };

    buildResult.RootObject["version"] = 3;
    // base object handler is separated, so we can do some testing on non-production databases
    // exact interface may want to be tweaked when we implement this
    var (rootObjId, convertedReferences) = await 
      Send(buildResult.RootObject, sendInfo, onOperationProgressed, ct)
      .ConfigureAwait(false);

    return new(rootObjId, convertedReferences, buildResult.ConversionResults);
  }
  
   public async Task<(string rootObjId, IReadOnlyDictionary<string, ObjectReference> convertedReferences)> Send(
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
    var sendResult = await operations
      .Send2(
        sendInfo.ServerUrl,
        sendInfo.ProjectId,
        account.token,
        commitObject,
        onProgressAction: new PassthroughProgress(args => sendProgress.Report(onOperationProgressed, args)),
        ct
      )
      .ConfigureAwait(false);

    sendConversionCache.StoreSendResult(sendInfo.ProjectId, sendResult.convertedReferences);

    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Linking version to model...", null));

    // 8 - Create the version (commit)
    using var apiClient = clientFactory.Create(account);
    _ = await apiClient
      .Version.Create(
        new CreateVersionInput(
          sendResult.rootObjId,
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
  IReadOnlyDictionary<string, ObjectReference> ConvertedReferences,
  IEnumerable<SendConversionResult> ConversionResults
);
