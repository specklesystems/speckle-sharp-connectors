using Speckle.Connectors.Utils.Builders;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Utils.Operations;

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
    using var execute = _activityFactory.Start();
    Speckle.Sdk.Api.GraphQL.Models.Version? version;
    Base? commitObject;
    HostObjectBuilderResult? res;
    // 2 - Check account exist
    Account account = _accountService.GetAccountWithServerUrlFallback(receiveInfo.AccountId, receiveInfo.ServerUrl);
    using Client apiClient = _clientFactory.Create(account);

    using (var _ = _activityFactory.Start("Receive version"))
    {
      version = await apiClient
        .Version.Get(receiveInfo.SelectedVersionId, receiveInfo.ModelId, receiveInfo.ProjectId, cancellationToken)
        .ConfigureAwait(false);
    }

    int totalCount = 1;

    using var transport = _serverTransportFactory.Create(account, receiveInfo.ProjectId);
    using (var _ = _activityFactory.Start("Receive objects"))
    {
      _progressDisplayManager.Begin();
      commitObject = await _operations
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
    }

    // 4 - Convert objects
    using (var _ = _activityFactory.Start("Convert"))
    {
      res = await _hostObjectBuilder
        .Build(commitObject, receiveInfo.ProjectName, receiveInfo.ModelName, onOperationProgressed, cancellationToken)
        .ConfigureAwait(false);
    }

    await apiClient
      .Version.Received(new(version.id, receiveInfo.ProjectId, receiveInfo.SourceApplication), cancellationToken)
      .ConfigureAwait(false);

    return res;
  }
}
