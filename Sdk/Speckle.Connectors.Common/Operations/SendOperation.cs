using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations.Send;
using Speckle.Connectors.Logging;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
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
  ISendOperationExecutor sendOperationExecutor,
  ISdkActivityFactory activityFactory,
  ISpeckleApplication speckleApplication,
  IIngestionProgressManagerFactory ingestionProgressManagerFactory
) : ISendOperation<T>
{
  public async Task<(SendOperationResult sendResult, string versionId)> Send(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    string? fileName,
    long? fileSizeBytes,
    string? versionMessage,
    IProgress<CardProgress> uiProgress,
    CancellationToken cancellationToken
  ) =>
    await SendViaIngestion(objects, sendInfo, fileName, fileSizeBytes, versionMessage, uiProgress, cancellationToken);

  private async Task<(SendOperationResult sendResult, string versionId)> SendViaIngestion(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    string? fileName,
    long? fileSizeBytes,
    string? versionMessage,
    IProgress<CardProgress> uiProgress,
    CancellationToken cancellationToken
  )
  {
    ModelIngestion ingestion = await sendInfo.Client.Ingestion.Create(
      new(
        sendInfo.ModelId,
        sendInfo.ProjectId,
        $"Sending from {speckleApplication.ApplicationAndVersion}",
        new(speckleApplication.Slug, speckleApplication.HostApplicationVersion, fileName, fileSizeBytes)
      ),
      cancellationToken
    );
    using var ingestionScope = ActivityScope.SetTag("modelIngestionId", ingestion.id);

    var ingestionProgress = ingestionProgressManagerFactory.CreateInstance(
      sendInfo.Client,
      ingestion,
      sendInfo.ProjectId,
      TimeSpan.FromSeconds(10),
      cancellationToken
    );

    AggregateProgress<CardProgress> progress = new(ingestionProgress, uiProgress);
    try
    {
      var sendPipeline = new Sdk.Pipelines.SendPipeline(
        sendInfo.Account,
        sendInfo.ProjectId,
        sendInfo.ModelId,
        ingestion.id,
        cancellationToken
      );
      var buildResult = await rootObjectBuilder.Build(
        objects,
        sendInfo.ProjectId,
        progress,
        sendPipeline,
        cancellationToken
      );

      buildResult.RootObject["version"] = 3;

      SendOperationResult result =
        new(buildResult.RootObject.id!, new Dictionary<Id, ObjectReference>(), buildResult.ConversionResults);

      string createdVersionId = await sendInfo.Client.Ingestion.Complete(
        new(ingestion.id, sendInfo.ProjectId, result.RootObjId, versionMessage),
        CancellationToken.None
      );

      return (result, createdVersionId);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      _ = await sendInfo.Client.Ingestion.FailWithCancel(
        new(ingestion.id, sendInfo.ProjectId, "User requested cancellation"),
        CancellationToken.None
      );
      throw;
    }
    catch (Exception ex)
    {
      _ = await sendInfo.Client.Ingestion.FailWithError(
        ModelIngestionFailedInput.FromException(ingestion.id, sendInfo.ProjectId, ex),
        CancellationToken.None
      );
      throw;
    }
  }

  // public async Task<SendOperationResult> ConvertAndSend(
  //   IReadOnlyList<T> objects,
  //   SendInfo sendInfo,
  //   IProgress<CardProgress> onOperationProgressed,
  //   CancellationToken ct = default
  // )
  // {
  //   // base object handler is separated, so we can do some testing on non-production databases
  //   // exact interface may want to be tweaked when we implement this
  //   var results = await threadContext.RunOnWorkerAsync(async () =>
  //   {
  //     SerializeProcessResults results = await SendObjects(
  //       buildResult.RootObject,
  //       sendInfo.ProjectId,
  //       sendInfo.Account,
  //       onOperationProgressed,
  //       ct
  //     );
  //
  //     return results;
  //   });
  // }

  public async Task<SerializeProcessResults> SendObjects(
    Base commitObject,
    string projectId,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    cancellationToken.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Uploading...", null));
    using var activity = activityFactory.Start("SendOperation");

    sendProgress.Begin();
    var sendResult = await sendOperationExecutor.Send(
      new Uri(account.serverInfo.url),
      projectId,
      account.token,
      commitObject,
      onProgressAction: new PassthroughProgress(args => sendProgress.Report(onOperationProgressed, args)),
      cancellationToken
    );

    sendConversionCache.StoreSendResult(projectId, sendResult.ConvertedReferences);

    cancellationToken.ThrowIfCancellationRequested();

    return sendResult;
  }
}

public record SendOperationResult(
  string RootObjId,
  IReadOnlyDictionary<Id, ObjectReference> ConvertedReferences,
  IReadOnlyList<SendConversionResult> ConversionResults
);
