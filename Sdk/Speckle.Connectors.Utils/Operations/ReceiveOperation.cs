using Speckle.Connectors.Utils.Builders;
using Speckle.Core.Api;
using Speckle.Core.Credentials;
using Speckle.Core.Models;
using Speckle.Core.Transports;
using Speckle.Logging;

namespace Speckle.Connectors.Utils.Operations;

public sealed class ReceiveOperation
{
  private readonly IHostObjectBuilder _hostObjectBuilder;
  private readonly ISyncToThread _syncToThread;
  private readonly AccountService _accountService;
  private readonly IServerTransportFactory _serverTransportFactory;

  public ReceiveOperation(
    IHostObjectBuilder hostObjectBuilder,
    ISyncToThread syncToThread,
    AccountService accountService,
    IServerTransportFactory serverTransportFactory
  )
  {
    _hostObjectBuilder = hostObjectBuilder;
    _syncToThread = syncToThread;
    _accountService = accountService;
    _serverTransportFactory = serverTransportFactory;
  }

  public async Task<HostObjectBuilderResult> Execute(
    ReceiveInfo receiveInfo,
    CancellationToken cancellationToken,
    Action<string, double?>? onOperationProgressed = null
  )
  {
    using var execute = SpeckleActivityFactory.Start();
    Speckle.Core.Api.GraphQL.Models.Version? version;
    Base? commitObject;
    HostObjectBuilderResult? res;
    // 2 - Check account exist
    Account account = _accountService.GetAccountWithServerUrlFallback(receiveInfo.AccountId, receiveInfo.ServerUrl);
    using Client apiClient = new(account);
    using (var receive = SpeckleActivityFactory.Start("Receive from server"))
    {
      // 3 - Get commit object from server
      version = await apiClient
        .Version.Get(receiveInfo.SelectedVersionId, receiveInfo.ModelId, receiveInfo.ProjectId, cancellationToken)
        .ConfigureAwait(false);
    }

    using (var receive = SpeckleActivityFactory.Start("Receive to transport"))
    {
      using var transport = _serverTransportFactory.Create(account, receiveInfo.ProjectId);
      commitObject = await Speckle
        .Core.Api.Operations.Receive(version.referencedObject, transport, cancellationToken: cancellationToken)
        .ConfigureAwait(false);

      cancellationToken.ThrowIfCancellationRequested();
    }

    using (var receive = SpeckleActivityFactory.Start("Convert"))
    {
      // 4 - Convert objects
      res = await _syncToThread
        .RunOnThread(() =>
        {
          return _hostObjectBuilder.Build(
            commitObject,
            receiveInfo.ProjectName,
            receiveInfo.ModelName,
            onOperationProgressed,
            cancellationToken
          );
        })
        .ConfigureAwait(false);
    }

    await apiClient
      .Version.Received(new(version.id, receiveInfo.ProjectId, receiveInfo.SourceApplication), cancellationToken)
      .ConfigureAwait(false);

    return res;
  }
}
