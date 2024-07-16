using Speckle.Connectors.Utils.Builders;
using Speckle.Core.Api;
using Speckle.Core.Credentials;
using Speckle.Core.Models;
using Speckle.Core.SchemaVersioning;
using Speckle.Core.Serialisation.TypeCache;
using Speckle.Core.Transports;
using SpeckleVersion = Speckle.Core.Api.GraphQL.Models.Version;
using SystemVersion = System.Version;

namespace Speckle.Connectors.Utils.Operations;

public sealed class ReceiveOperation
{
  private readonly IHostObjectBuilder _hostObjectBuilder;
  private readonly ISyncToThread _syncToThread;
  private readonly AccountService _accountService;
  private readonly ITypeCache _typeCache;
  private readonly ISchemaObjectUpgradeManager<Base, Base> _schemaObjectUpgraderManager;

  public ReceiveOperation(
    IHostObjectBuilder hostObjectBuilder,
    ISyncToThread syncToThread,
    AccountService accountService,
    ITypeCache typeCache,
    ISchemaObjectUpgradeManager<Base, Base> schemaObjectUpgraderManager
  )
  {
    _hostObjectBuilder = hostObjectBuilder;
    _syncToThread = syncToThread;
    _accountService = accountService;
    _typeCache = typeCache;
    _schemaObjectUpgraderManager = schemaObjectUpgraderManager;
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

    using ServerTransport transport = new(account, receiveInfo.ProjectId);
    Base commitObject = await Speckle
      .Core.Api.Operations.Receive(version.referencedObject, _typeCache, _schemaObjectUpgraderManager, version.SchemaVersion ?? new SystemVersion(SpeckleVersion.EARLIEST_SCHEMA_VERSION_STRING), transport, cancellationToken: cancellationToken)
      .ConfigureAwait(false);

    cancellationToken.ThrowIfCancellationRequested();

    // 4 - Convert objects
    var res = await _syncToThread
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

    await apiClient
      .Version.Received(new(version.id, receiveInfo.ProjectId, receiveInfo.SourceApplication), cancellationToken)
      .ConfigureAwait(false);

    return res;
  }
}
