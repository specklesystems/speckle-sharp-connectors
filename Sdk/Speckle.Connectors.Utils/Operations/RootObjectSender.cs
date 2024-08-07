using Speckle.Connectors.Utils.Caching;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Utils.Operations;

/// <summary>
/// Default implementation of the <see cref="IRootObjectSender"/> which takes a <see cref="Base"/> and sends
/// it to a server described by the parameters in the <see cref="Send"/> method
/// </summary>
/// POC: we have a generic RootObjectSender but we're not using it everywhere. It also appears to need some specialisation or at least
/// a way to get the application name, so RevitContext is being used in the revit version but we could probably inject that as a IHostAppContext maybe?
[GenerateAutoInterface]
public sealed class RootObjectSender : IRootObjectSender
{
  // POC: Revisit this factory pattern, I think we could solve this higher up by injecting a scoped factory for `SendOperation` in the SendBinding
  private readonly IServerTransportFactory _transportFactory;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly AccountService _accountService;
  private readonly ISendHelper _sendHelper;

  public RootObjectSender(
    IServerTransportFactory transportFactory,
    ISendConversionCache sendConversionCache,
    AccountService accountService,
    ISendHelper sendHelper
  )
  {
    _transportFactory = transportFactory;
    _sendConversionCache = sendConversionCache;
    _accountService = accountService;
    _sendHelper = sendHelper;
  }

  /// <summary>
  /// Contract for the send operation that handles an assembled <see cref="Base"/> object.
  /// In production, this will send to a server.
  /// In testing, this could send to a sqlite db or just save to a dictionary.
  /// </summary>
  public async Task<(string rootObjId, Dictionary<string, ObjectReference> convertedReferences)> Send(
    Base commitObject,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    ct.ThrowIfCancellationRequested();

    onOperationProgressed?.Invoke("Uploading...", null);

    Account account = _accountService.GetAccountWithServerUrlFallback(sendInfo.AccountId, sendInfo.ServerUrl);

    using var transport = _transportFactory.Create(account, sendInfo.ProjectId, 60, null);
    var sendResult = await _sendHelper.Send(commitObject, transport, true, null, ct).ConfigureAwait(false);

    _sendConversionCache.StoreSendResult(sendInfo.ProjectId, sendResult.convertedReferences);

    ct.ThrowIfCancellationRequested();

    onOperationProgressed?.Invoke("Linking version to model...", null);

    // 8 - Create the version (commit)
    using var apiClient = new Client(account);
    _ = await apiClient
      .Version.Create(
        new CreateVersionInput(
          sendResult.rootObjId,
          sendInfo.ModelId,
          sendInfo.ProjectId,
          sourceApplication: sendInfo.SourceApplication
        ),
        ct
      )
      .ConfigureAwait(true);

    return sendResult;
  }
}
