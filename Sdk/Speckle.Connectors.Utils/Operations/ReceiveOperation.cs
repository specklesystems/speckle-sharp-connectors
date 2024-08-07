using Speckle.Connectors.Utils.Builders;
using Speckle.Core.Api;
using Speckle.Core.Credentials;
using Speckle.Core.Models;
using Speckle.Core.Transports;

namespace Speckle.Connectors.Utils.Operations;

public sealed class ReceiveOperation
{
  private readonly IHostObjectBuilder _hostObjectBuilder;
  private readonly AccountService _accountService;
  private readonly IServerTransportFactory _serverTransportFactory;

  public ReceiveOperation(
    IHostObjectBuilder hostObjectBuilder,
    AccountService accountService,
    IServerTransportFactory serverTransportFactory
  )
  {
    _hostObjectBuilder = hostObjectBuilder;
    _accountService = accountService;
    _serverTransportFactory = serverTransportFactory;
  }

  public async Task<HostObjectBuilderResult> Execute(
    ReceiveInfo receiveInfo,
    CancellationToken cancellationToken,
    Action<string, double?>? onOperationProgressed = null
  )
  {
    // 2 - Check account exist
    Account account = _accountService.GetAccountWithServerUrlFallback(receiveInfo.AccountId, receiveInfo.ServerUrl);

    // 3 - Get commit object from server
    using Client apiClient = new(account);
    var version = await apiClient
      .Version.Get(receiveInfo.SelectedVersionId, receiveInfo.ModelId, receiveInfo.ProjectId, cancellationToken)
      .ConfigureAwait(false);
    int totalCount = 1;

    using var transport = _serverTransportFactory.Create(account, receiveInfo.ProjectId);

    Base commitObject = await Speckle
      .Core.Api.Operations.Receive(
        version.referencedObject,
        transport,
        onProgressAction: dict =>
        {
          // NOTE: this looks weird for the user, as when deserialization kicks in, the progress bar will go down, and then start progressing again.
          // This is something we're happy to live with until we refactor the whole receive pipeline.
          onOperationProgressed?.Invoke($"Downloading and deserializing", dict.Values.Average() / totalCount);
        },
        onTotalChildrenCountKnown: c => totalCount = c,
        cancellationToken: cancellationToken
      )
      .ConfigureAwait(false);

    cancellationToken.ThrowIfCancellationRequested();

    // 4 - Convert objects
    var res = await _hostObjectBuilder
      .Build(commitObject, receiveInfo.ProjectName, receiveInfo.ModelName, onOperationProgressed, cancellationToken)
      .ConfigureAwait(false);

    await apiClient
      .Version.Received(new(version.id, receiveInfo.ProjectId, receiveInfo.SourceApplication), cancellationToken)
      .ConfigureAwait(false);

    return res;
  }
}
