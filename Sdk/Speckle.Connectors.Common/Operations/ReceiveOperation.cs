using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Logging;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

public sealed class ReceiveOperation
{
  private readonly IHostObjectBuilder _hostObjectBuilder;
  private readonly AccountService _accountService;
  private readonly IServerTransportFactory _serverTransportFactory;
  private readonly IProgressDisplayManager _progressDisplayManager;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly IOperations _operations;
  private readonly IClientFactory _clientFactory;

  public ReceiveOperation(
    IHostObjectBuilder hostObjectBuilder,
    AccountService accountService,
    IServerTransportFactory serverTransportFactory,
    IProgressDisplayManager progressDisplayManager,
    ISdkActivityFactory activityFactory,
    IOperations operations,
    IClientFactory clientFactory
  )
  {
    _hostObjectBuilder = hostObjectBuilder;
    _accountService = accountService;
    _serverTransportFactory = serverTransportFactory;
    _progressDisplayManager = progressDisplayManager;
    _activityFactory = activityFactory;
    _operations = operations;
    _clientFactory = clientFactory;
  }

  public async Task<HostObjectBuilderResult> Execute(
    ReceiveInfo receiveInfo,
    CancellationToken cancellationToken,
    Action<string, double?>? onOperationProgressed = null
  )
  {
    using var execute = _activityFactory.Start("Receive Operation");
    execute?.SetTag("receiveInfo", receiveInfo);
    // 2 - Check account exist
    Account account = _accountService.GetAccountWithServerUrlFallback(receiveInfo.AccountId, receiveInfo.ServerUrl);
    using Client apiClient = _clientFactory.Create(account);
    _activityFactory.SetTag(Consts.USER_ID, account.GetHashedEmail());
    execute?.SetBaggage(Consts.USER_ID, account.GetHashedEmail());

    var version = await apiClient
      .Version.Get(receiveInfo.SelectedVersionId, receiveInfo.ModelId, receiveInfo.ProjectId, cancellationToken)
      .ConfigureAwait(false);

    int totalCount = 1;

    using var transport = _serverTransportFactory.Create(account, receiveInfo.ProjectId);

    _progressDisplayManager.Begin();
    Base? commitObject = await _operations
      .Receive(
        version.referencedObject,
        transport,
        onProgressAction: dict =>
        {
          if (!_progressDisplayManager.ShouldUpdate())
          {
            return;
          }

          // NOTE: this looks weird for the user, as when deserialization kicks in, the progress bar will go down, and then start progressing again.
          // This is something we're happy to live with until we refactor the whole receive pipeline.
          var args = dict.FirstOrDefault();
          if (args is null)
          {
            return;
          }
          switch (args.ProgressEvent)
          {
            case ProgressEvent.DownloadBytes:
              onOperationProgressed?.Invoke(
                $"Downloading ({_progressDisplayManager.CalculateSpeed(args)})",
                _progressDisplayManager.CalculatePercentage(args)
              );
              break;
            case ProgressEvent.DownloadObject:
              onOperationProgressed?.Invoke("Downloading Root Object...", null);
              break;
            case ProgressEvent.DeserializeObject:
              onOperationProgressed?.Invoke(
                $"Deserializing ({_progressDisplayManager.CalculateSpeed(args)})",
                _progressDisplayManager.CalculatePercentage(args)
              );
              break;
          }
        },
        onTotalChildrenCountKnown: c => totalCount = c,
        cancellationToken: cancellationToken
      )
      .ConfigureAwait(false);

    cancellationToken.ThrowIfCancellationRequested();

    // 4 - Convert objects
    HostObjectBuilderResult? res = await ConvertObjects(
        commitObject,
        receiveInfo,
        onOperationProgressed,
        cancellationToken
      )
      .ConfigureAwait(false);

    await apiClient
      .Version.Received(new(version.id, receiveInfo.ProjectId, receiveInfo.SourceApplication), cancellationToken)
      .ConfigureAwait(false);

    return res;
  }

  private async Task<HostObjectBuilderResult> ConvertObjects(
    Base commitObject,
    ReceiveInfo receiveInfo,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var conversionActivity = _activityFactory.Start("ReceiveOperation.ConvertObjects");
    conversionActivity?.SetTag("smellsLikeV2Data", commitObject.SmellsLikeV2Data());
    conversionActivity?.SetTag("receiveInfo.serverUrl", receiveInfo.ServerUrl);
    conversionActivity?.SetTag("receiveInfo.projectId", receiveInfo.ProjectId);
    conversionActivity?.SetTag("receiveInfo.modelId", receiveInfo.ModelId);
    conversionActivity?.SetTag("receiveInfo.selectedVersionId", receiveInfo.SelectedVersionId);
    conversionActivity?.SetTag("receiveInfo.sourceApplication", receiveInfo.SourceApplication);

    try
    {
      var res = await _hostObjectBuilder
        .Build(commitObject, receiveInfo.ProjectName, receiveInfo.ModelName, onOperationProgressed, cancellationToken)
        .ConfigureAwait(false);
      conversionActivity?.SetStatus(SdkActivityStatusCode.Ok);
      return res;
    }
    catch (Exception ex)
    {
      conversionActivity?.RecordException(ex);
      conversionActivity?.SetStatus(SdkActivityStatusCode.Error);
      throw;
    }
  }
}
