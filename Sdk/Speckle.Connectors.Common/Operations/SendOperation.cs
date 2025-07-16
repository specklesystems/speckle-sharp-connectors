using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Threading;
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
    var buildResult = await Build(objects, sendInfo.ProjectId, onOperationProgressed, ct);
    // base object handler is separated, so we can do some testing on non-production databases
    // exact interface may want to be tweaked when we implement this
    var (results, versionId) = await threadContext.RunOnWorkerAsync(
      () =>
        Send(
          buildResult.RootObject,
          sendInfo.ProjectId,
          sendInfo.ModelId,
          sendInfo.SourceApplication,
          sendInfo.Account,
          onOperationProgressed,
          ct
        )
    );
    ct.ThrowIfCancellationRequested();
    return new(results.RootId, versionId, results.ConvertedReferences, buildResult.ConversionResults);
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<T> objects,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var buildResult = await rootObjectBuilder.Build(objects, projectId, onOperationProgressed, ct);
    ct.ThrowIfCancellationRequested();
    // POC: Jonathon asks on behalf of willow twin - let's explore how this can work
    // buildResult.RootObject["@report"] = new Report { ConversionResults = buildResult.ConversionResults };
    buildResult.RootObject["version"] = 3;
    return buildResult;
  }

  public async Task<(SerializeProcessResults, string)> Send(
    Base commitObject,
    string projectId,
    string modelId,
    string sourceApplication,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default,
    string? versionMessage = null
  )
  {
    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Uploading...", null));

    using var activity = activityFactory.Start("SendOperation");

    sendProgress.Begin();
    var sendResult = await operations.Send2(
      new Uri(account.serverInfo.url),
      projectId,
      account.token,
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
      account,
      ct
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
