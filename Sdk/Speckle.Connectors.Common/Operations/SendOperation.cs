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
  IAccountService accountService,
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
    CancellationToken ct = default
  )
  {
    ct.ThrowIfCancellationRequested();
    var buildResult = await Build(objects, sendInfo, onOperationProgressed, ct);
    // base object handler is separated, so we can do some testing on non-production databases
    // exact interface may want to be tweaked when we implement this
    var (results, versionId) = await threadContext.RunOnWorkerAsync(
      () => Send(buildResult.RootObject, sendInfo, onOperationProgressed, ct)
    );
    ct.ThrowIfCancellationRequested();
    return new(results.RootId, versionId, results.ConvertedReferences, buildResult.ConversionResults);
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var buildResult = await rootObjectBuilder.Build(objects, sendInfo, onOperationProgressed, ct);
    ct.ThrowIfCancellationRequested();
    // POC: Jonathon asks on behalf of willow twin - let's explore how this can work
    // buildResult.RootObject["@report"] = new Report { ConversionResults = buildResult.ConversionResults };
    buildResult.RootObject["version"] = 3;
    return buildResult;
  }

  public async Task<(SerializeProcessResults, string)> Send(
    Base commitObject,
    Uri serverUrl,
    string projectId,
    string modelId,
    string token,
    string sourceApplication,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Uploading...", null));

    using var activity = activityFactory.Start("SendOperation");

    sendProgress.Begin();
    var sendResult = await operations.Send2(
      serverUrl,
      projectId,
      token,
      commitObject,
      onProgressAction: new PassthroughProgress(args => sendProgress.Report(onOperationProgressed, args)),
      ct
    );

    sendConversionCache.StoreSendResult(projectId, sendResult.ConvertedReferences);

    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Linking version to model...", null));

    // 8 - Create the version (commit)
    var versionId = await sendOperationVersionRecorder.RecordVersion(
      sendResult.RootId,
      modelId,
      projectId,
      sourceApplication,
      serverUrl,
      token,
      ct
    );

    return (sendResult, versionId);
  }

  public Task<(SerializeProcessResults, string)> Send(
    Base commitObject,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    ct.ThrowIfCancellationRequested();
    onOperationProgressed.Report(new("Uploading...", null));
    Account account = accountService.GetAccountWithServerUrlFallback(sendInfo.AccountId, sendInfo.ServerUrl);
    return Send(
      commitObject,
      sendInfo.ServerUrl,
      sendInfo.ProjectId,
      sendInfo.ModelId,
      account.token,
      sendInfo.SourceApplication,
      onOperationProgressed,
      ct
    );
  }
}

public record SendOperationResult(
  string RootObjId,
  string VersionId,
  IReadOnlyDictionary<Id, ObjectReference> ConvertedReferences,
  IReadOnlyList<SendConversionResult> ConversionResults
);
