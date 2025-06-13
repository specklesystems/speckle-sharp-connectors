using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Logging;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

public class GrasshopperReceiveOperation(
  IServerTransportFactory serverTransportFactory,
  IProgressDisplayManager progressDisplayManager,
  ISdkActivityFactory activityFactory,
  IOperations operations,
  IClientFactory clientFactory
)
{
  public async Task<Base> ReceiveCommitObject(
    ReceiveInfo receiveInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var execute = activityFactory.Start("Receive Operation");
    execute?.SetTag("receiveInfo", receiveInfo);
    // 2 - Check account exist
    Account account = receiveInfo.Account;
    using IClient apiClient = clientFactory.Create(account);
    using var userScope = ActivityScope.SetTag(Consts.USER_ID, account.GetHashedEmail());

    Speckle.Sdk.Api.GraphQL.Models.Version? version = await apiClient
      .Version.Get(receiveInfo.SelectedVersionId, receiveInfo.ProjectId, cancellationToken)
      .ConfigureAwait(false);

    if (version?.referencedObject is not string receivedVersion)
    {
      throw new InvalidOperationException($"Could not retrieve version from server.");
    }

    using var transport = serverTransportFactory.Create(account, receiveInfo.ProjectId);

    double? previousPercentage = null;
    progressDisplayManager.Begin();
    Base commitObject = await operations
      .Receive2(
        new Uri(account.serverInfo.url),
        receiveInfo.ProjectId,
        receivedVersion,
        account.token,
        onProgressAction: new PassthroughProgress(args =>
        {
          if (args.ProgressEvent == ProgressEvent.CacheCheck || args.ProgressEvent == ProgressEvent.DownloadBytes)
          {
            switch (args.ProgressEvent)
            {
              case ProgressEvent.CacheCheck:
                previousPercentage = progressDisplayManager.CalculatePercentage(args);
                break;
            }
          }
          if (!progressDisplayManager.ShouldUpdate())
          {
            return;
          }

          switch (args.ProgressEvent)
          {
            case ProgressEvent.CacheCheck:
            case ProgressEvent.DownloadBytes:
              onOperationProgressed.Report(new("Checking and Downloading... ", previousPercentage));
              break;
            case ProgressEvent.DeserializeObject:
              onOperationProgressed.Report(new("Deserializing ...", progressDisplayManager.CalculatePercentage(args)));
              break;
          }
        }),
        cancellationToken: cancellationToken
      )
      .ConfigureAwait(false);

    await apiClient
      .Version.Received(new(version.id, receiveInfo.ProjectId, receiveInfo.SourceApplication), cancellationToken)
      .ConfigureAwait(false);
    return commitObject;
  }
}
