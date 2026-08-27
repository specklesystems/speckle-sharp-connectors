using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Threading;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public sealed class ReceiveOperation(
  IHostObjectBuilder hostObjectBuilder,
  IReceiveProgress receiveProgress,
  ISdkActivityFactory activityFactory,
  IOperations operations,
  IReceiveVersionRetriever receiveVersionRetriever,
  IThreadContext threadContext,
  IArtifactReceiver? artifactReceiver = null,
  IArtifactHostObjectBuilder? artifactHostObjectBuilder = null
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
    Account account = receiveInfo.Account;
    using var userScope = UserActivityScope.AddUserScope(account);
    var version = await receiveVersionRetriever.GetVersion(account, receiveInfo, cancellationToken);
    cancellationToken.ThrowIfCancellationRequested();

    // Speckle 4.0 artefact path: when the connector registered an IArtifactReceiver, probe the v2 data endpoints for
    // this version's parquet bundle. If present, either bake it DIRECTLY (a dedicated IArtifactHostObjectBuilder, e.g.
    // Rhino) or reconstruct a Base graph and run the v1 host builder (e.g. Revit). Returns to the v1 path when there's
    // no bundle (legacy version / old server). Detection is by the artefacts endpoint returning files.
    if (artifactReceiver != null)
    {
      receiveProgress.Begin();
      var bundle = await artifactReceiver
        .TryGetBundleAsync(account, receiveInfo, onOperationProgressed, cancellationToken)
        .ConfigureAwait(false);
      if (bundle != null)
      {
        HostObjectBuilderResult artefactRes;
        if (artifactHostObjectBuilder != null)
        {
          // direct-bake path: parquet → host doc, no Base reconstruction.
          artefactRes = await artifactHostObjectBuilder
            .Build(
              bundle,
              new ArtefactReceiveTarget(
                receiveInfo.ProjectId,
                receiveInfo.ProjectName,
                receiveInfo.ModelId,
                receiveInfo.ModelName
              ),
              onOperationProgressed,
              cancellationToken
            )
            .ConfigureAwait(false);
        }
        else
        {
          // OBSOLETE reconstruction path — kept only so a connector without a direct-bake builder still receives.
          // New-object-model receives register an IArtifactHostObjectBuilder; do not lean on this branch for new work.
#pragma warning disable CS0618
          var artefactRoot = await threadContext.RunOnWorkerAsync(() =>
            Task.FromResult(artifactReceiver.Reconstruct(bundle, cancellationToken))
          );
#pragma warning restore CS0618
          artefactRes = await ConvertObjects(artefactRoot, receiveInfo, onOperationProgressed, cancellationToken)
            .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await receiveVersionRetriever.VersionReceived(account, version, receiveInfo, cancellationToken);
        return artefactRes;
      }
    }

    cancellationToken.ThrowIfCancellationRequested();
    var commitObject = await threadContext.RunOnWorkerAsync(() =>
      ReceiveData(account, version, receiveInfo, onOperationProgressed, cancellationToken)
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
    receiveProgress.Begin();

    if (version.referencedObject is null)
    {
      throw new SpeckleException("Version referenced object is null and cannot do a receive operation.");
    }
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
    conversionActivity?.SetTag("receiveInfo.receivingApplicationSlug", receiveInfo.ReceivingApplicationSlug);

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
