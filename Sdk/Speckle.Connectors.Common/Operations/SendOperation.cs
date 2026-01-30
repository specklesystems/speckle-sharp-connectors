using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations.Send;
using Speckle.Connectors.Common.Threading;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
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
  ISdkActivityFactory activityFactory,
  IThreadContext threadContext,
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
  )
  {
    bool useModelIngestionSend = await CheckUseModelIngestionSend(sendInfo);
    if (useModelIngestionSend)
    {
      return await SendViaIngestion(objects, sendInfo, null, null, null, uiProgress, cancellationToken);
    }
    else
    {
      return await SendViaVersionCreate(objects, sendInfo, null, uiProgress, cancellationToken);
    }
  }

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
    var ingestionProgress = ingestionProgressManagerFactory.CreateInstance(
      sendInfo.Client,
      ingestion,
      sendInfo.ProjectId,
      cancellationToken
    );
    AggregateProgress<CardProgress> progress = new(ingestionProgress, uiProgress);
    try
    {
      SendOperationResult result = await ConvertAndSend(objects, sendInfo, progress, cancellationToken);

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

  private async Task<(SendOperationResult sendResult, string versionId)> SendViaVersionCreate(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    string? versionMessage,
    IProgress<CardProgress> progress,
    CancellationToken cancellationToken
  )
  {
    SendOperationResult result = await ConvertAndSend(objects, sendInfo, progress, cancellationToken);

    Version version = await sendInfo.Client.Version.Create(
      new(
        result.RootObjId,
        sendInfo.ModelId,
        sendInfo.ProjectId,
        sourceApplication: speckleApplication.Slug,
        message: versionMessage
      ),
      cancellationToken
    );
    return (result, version.id);
  }

  public async Task<SendOperationResult> ConvertAndSend(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var buildResult = await Build(objects, sendInfo.ProjectId, onOperationProgressed, ct);
    // base object handler is separated, so we can do some testing on non-production databases
    // exact interface may want to be tweaked when we implement this
    var results = await threadContext.RunOnWorkerAsync(async () =>
    {
      SerializeProcessResults results = await SendObjects(
        buildResult.RootObject,
        sendInfo.ProjectId,
        sendInfo.Account,
        onOperationProgressed,
        ct
      );

      return results;
    });
    return new(results.RootId, results.ConvertedReferences, buildResult.ConversionResults);
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<T> objects,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var buildResult = await rootObjectBuilder.Build(objects, projectId, onOperationProgressed, cancellationToken);
    // POC: Jonathon asks on behalf of willow twin - let's explore how this can work
    // buildResult.RootObject["@report"] = new Report { ConversionResults = buildResult.ConversionResults };
    buildResult.RootObject["version"] = 3;
    return buildResult;
  }

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

  /// <summary>
  /// There are three paths for this function:
  /// <ul>
  /// <li>Server Supports ingestion, and the user has permission to create an ingetsion => returns <see langword="true"/></li>
  /// <li> Server doesn't support ingestions (i.e. public server or old servers) => returns <see langword="false"/></li>
  /// <li> Server Supports ingestions, but the user doesn't have permission to create ingestion => throws <see cref="WorkspacePermissionException"/></li>
  /// </ul>
  /// </summary>
  /// <param name="sendInfo"></param>
  /// <returns><see langword="true"/> if we should use model ingestion based send functions, false</returns>
  /// <exception cref="WorkspacePermissionException">Thrown if the server supports model ingestion, but for other reasons we won't beable to create an ingestion</exception>
  private static async Task<bool> CheckUseModelIngestionSend(SendInfo sendInfo)
  {
    bool useModelIngestionSend = true;
    try
    {
      PermissionCheckResult permissionCheck = await sendInfo.Client.Model.CanCreateModelIngestion(
        sendInfo.ProjectId,
        sendInfo.ModelId
      );
      permissionCheck.EnsureAuthorised();
    }
    catch (AggregateException ex) when (ex.InnerExceptions.OfType<SpeckleGraphQLInvalidQueryException>().Any())
    {
      // CanCreateModelIngestion will throw this if the server is too old and doesn't support model ingestion API
      useModelIngestionSend = false;
    }

    return useModelIngestionSend;
  }
}

public record SendOperationResult(
  string RootObjId,
  IReadOnlyDictionary<Id, ObjectReference> ConvertedReferences,
  IReadOnlyList<SendConversionResult> ConversionResults
);
