using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Logging;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

public class GrasshopperReceiveOperation
{
  private readonly IServerTransportFactory _serverTransportFactory;
  private readonly IProgressDisplayManager _progressDisplayManager;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ISpeckleApplication _application;
  private readonly IOperations _operations;
  private readonly IClientFactory _clientFactory;

  public GrasshopperReceiveOperation(
    IServerTransportFactory serverTransportFactory,
    IProgressDisplayManager progressDisplayManager,
    ISdkActivityFactory activityFactory,
    ISpeckleApplication application,
    IOperations operations,
    IClientFactory clientFactory
  )
  {
    _serverTransportFactory = serverTransportFactory;
    _progressDisplayManager = progressDisplayManager;
    _activityFactory = activityFactory;
    _application = application;
    _operations = operations;
    _clientFactory = clientFactory;
  }

  public async Task<Base> ReceiveCommitObject(
    ReceiveInfo receiveInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var execute = _activityFactory.Start("Receive Operation");
    execute?.SetTag("receiveInfo", receiveInfo);
    // 2 - Check account exist
    var account = receiveInfo.Account;
    using IClient apiClient = _clientFactory.Create(account);
    using var userScope = ActivityScope.SetTag(Consts.USER_ID, account.GetHashedEmail());

    Speckle.Sdk.Api.GraphQL.Models.Version? version = await apiClient
      .Version.Get(receiveInfo.SelectedVersionId, receiveInfo.ProjectId, cancellationToken)
      .ConfigureAwait(false);

    if (version?.referencedObject is not string receivedVersion)
    {
      throw new InvalidOperationException($"Could not retrieve version from server.");
    }

    using var transport = _serverTransportFactory.Create(account, receiveInfo.ProjectId);

    double? previousPercentage = null;
    _progressDisplayManager.Begin();
    Base commitObject = await _operations
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
                previousPercentage = _progressDisplayManager.CalculatePercentage(args);
                break;
            }
          }
          if (!_progressDisplayManager.ShouldUpdate())
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
              onOperationProgressed.Report(new("Deserializing ...", _progressDisplayManager.CalculatePercentage(args)));
              break;
          }
        }),
        cancellationToken: cancellationToken
      )
      .ConfigureAwait(false);

    await apiClient
      .Version.Received(new(version.id, receiveInfo.ProjectId, _application.Slug), cancellationToken)
      .ConfigureAwait(false);
    return commitObject;
  }
}
