using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Logging;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;
using Speckle.Sdk.Serialisation.V2.Send;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public sealed class SendOperation<T>(
  IRootObjectBuilder<T> rootObjectBuilder,
  ISendConversionCache sendConversionCache,
  ISendProgress sendProgress,
  IOperations operations,
  ISendOperationVersionRecorder sendOperationVersionRecorder,
  ISdkActivityFactory activityFactory,
  IThreadContext threadContext
) : ISendOperation<T>
{
  public async Task<SendOperationResult> Execute(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default,
    string? versionMessage = null
  )
  {
    ct.ThrowIfCancellationRequested();
    var buildResult = await rootObjectBuilder.Build(objects, sendInfo, onOperationProgressed, ct);

    ct.ThrowIfCancellationRequested();
    // POC: Jonathon asks on behalf of willow twin - let's explore how this can work
    // buildResult.RootObject["@report"] = new Report { ConversionResults = buildResult.ConversionResults };

    buildResult.RootObject["version"] = 3;
    // base object handler is separated, so we can do some testing on non-production databases
    // exact interface may want to be tweaked when we implement this
    var (results, versionId) = await threadContext.RunOnWorkerAsync(
      () => Send(buildResult.RootObject, sendInfo, onOperationProgressed, ct, versionMessage)
    );
    ct.ThrowIfCancellationRequested();

    return new(results.RootId, versionId, results.ConvertedReferences, buildResult.ConversionResults);
  }

  public async Task<(SerializeProcessResults, string)> Send(
    Base commitObject,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default,
    string? versionMessage = null
  )
  {
    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Uploading...", null));

    Account account = sendInfo.Account;
    using var userScope = ActivityScope.SetTag(Consts.USER_ID, account.GetHashedEmail());
    using var activity = activityFactory.Start("SendOperation");

    sendProgress.Begin();
    var sendResult = await operations.Send2(
      new(sendInfo.Account.serverInfo.url),
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
    var versionId = await sendOperationVersionRecorder.RecordVersion(
      sendResult.RootId,
      sendInfo,
      account,
      ct,
      versionMessage // DUI3 connectors handle it on UI as post action, GH gets it from component and sets
    );
    return (sendResult, versionId);
  }
}

public record SendOperationResult(
  string RootObjId,
  string VersionId,
  IReadOnlyDictionary<Id, ObjectReference> ConvertedReferences,
  IReadOnlyList<SendConversionResult> ConversionResults
);
