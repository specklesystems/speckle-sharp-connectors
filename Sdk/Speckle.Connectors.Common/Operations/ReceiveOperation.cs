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
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var execute = _activityFactory.Start("Receive Operation");
    execute?.SetTag("receiveInfo", receiveInfo);
    // 2 - Check account exist
    Account account = _accountService.GetAccountWithServerUrlFallback(receiveInfo.AccountId, receiveInfo.ServerUrl);
    using Client apiClient = _clientFactory.Create(account);
    using var userScope = ActivityScope.SetTag(Consts.USER_ID, account.GetHashedEmail());

    var version = await apiClient
      .Version.Get(receiveInfo.SelectedVersionId, receiveInfo.ModelId, receiveInfo.ProjectId, cancellationToken)
      .ConfigureAwait(false);

    using var transport = _serverTransportFactory.Create(account, receiveInfo.ProjectId);

    double? previousPercentage = null;
    string previousSpeed = string.Empty;
    _progressDisplayManager.Begin();
    Base? commitObject = await _operations
      .Receive2(
        new Uri(account.serverInfo.url),
        receiveInfo.ProjectId,
        version.referencedObject,
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
              case ProgressEvent.DownloadBytes:
                previousSpeed = _progressDisplayManager.CalculateSpeed(args);
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
              onOperationProgressed.Report(new($"Checking and Downloading... ({previousSpeed})", previousPercentage));
              break;
            case ProgressEvent.DeserializeObject:
              onOperationProgressed.Report(
                new(
                  $"Deserializing ({_progressDisplayManager.CalculateSpeed(args)})",
                  _progressDisplayManager.CalculatePercentage(args)
                )
              );
              break;
          }
        }),
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
    IProgress<CardProgress> onOperationProgressed,
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
      HostObjectBuilderResult res = await _hostObjectBuilder
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
