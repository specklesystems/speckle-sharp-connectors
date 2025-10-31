using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Threading;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;
using Speckle.Sdk.Serialisation.V2.Send;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public sealed class SendOperation<T>(
  IRootObjectBuilder<T> rootObjectBuilder,
  ISendConversionCache sendConversionCache,
  ISendProgress sendProgress,
  ISendOperationExecutor sendOperationExecutor,
  ISendOperationVersionRecorder sendOperationVersionRecorder,
  ISdkActivityFactory activityFactory,
  IClientFactory clientFactory,
  IThreadContext threadContext
) : ISendOperation<T>
{
  public async Task<SendOperationResult> Execute(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    string? versionMessage,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct
  )
  {
    using var apiClient = clientFactory.Create(sendInfo.Account);

    Ingest ingest = await apiClient.Ingest.Create(
      new IngestCreateInput(
        "My Rhino File",
        20,
        sendInfo.ModelId,
        sendInfo.ProjectId,
        sendInfo.SourceApplication,
        "1234",
        new Dictionary<string, object?>()
      ),
      ct
    );

    DateTime lastUpdate = new DateTime();
    var ingestProgress = new Progress<CardProgress>(x =>
    {
      onOperationProgressed.Report(x);
      var updateInput = new IngestUpdateInput(
        ingest.id,
        x.Progress is null ? null : Math.Round((float)x.Progress.Value, 3),
        x.Status,
        ingest.projectId
      );

      var now = DateTime.Now;
      var elapsedMs = (now - lastUpdate).Milliseconds;
      if (elapsedMs > 500)
      {
        lastUpdate = now;
        _ = apiClient.Ingest.Update(updateInput, ct).Result;
      }
    });

    ct.ThrowIfCancellationRequested();
    var buildResult = await Build(objects, sendInfo.ProjectId, ingestProgress, ct);
    // base object handler is separated, so we can do some testing on non-production databases
    // exact interface may want to be tweaked when we implement this
    var (results, version) = await threadContext.RunOnWorkerAsync(
      () => Send(buildResult.RootObject, ingest, versionMessage, apiClient, ingestProgress, ct)
    );
    ct.ThrowIfCancellationRequested();
    return new(results.RootId, version.id, results.ConvertedReferences, buildResult.ConversionResults);
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

  public async Task<(SerializeProcessResults, Version)> Send(
    Base commitObject,
    Ingest ingest,
    string? versionMessage,
    IClient apiClient,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Uploading...", null));

    using var userScope = UserActivityScope.AddUserScope(apiClient.Account);
    using var activity = activityFactory.Start("SendOperation");

    sendProgress.Begin();
    var sendResult = await sendOperationExecutor.Send(
      apiClient.ServerUrl,
      ingest.projectId,
      apiClient.Account.token,
      commitObject,
      onProgressAction: new PassthroughProgress(args => sendProgress.Report(onOperationProgressed, args)),
      ct
    );

    sendConversionCache.StoreSendResult(ingest.projectId, sendResult.ConvertedReferences);

    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Linking version to model...", null));

    // 8 - Create the version (commit)
    var version = await sendOperationVersionRecorder.RecordVersion(
      sendResult.RootId,
      ingest,
      versionMessage,
      apiClient,
      ct
    );

    return (sendResult, version);
  }
}

public record SendOperationResult(
  string RootObjId,
  string VersionId,
  IReadOnlyDictionary<Id, ObjectReference> ConvertedReferences,
  IReadOnlyList<SendConversionResult> ConversionResults
);
