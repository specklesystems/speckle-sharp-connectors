using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Logging;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

/// <summary>
/// Default implementation of the <see cref="IRootObjectSender"/> which takes a <see cref="Base"/> and sends
/// it to a server described by the parameters in the <see cref="Send"/> method
/// </summary>
/// POC: we have a generic RootObjectSender but we're not using it everywhere. It also appears to need some specialisation or at least
/// a way to get the application name, so RevitContext is being used in the revit version but we could probably inject that as a IHostAppContext maybe?
[GenerateAutoInterface]
public sealed class RootObjectSender : IRootObjectSender
{
  private readonly IServerTransportFactory _transportFactory;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly AccountService _accountService;
  private readonly IProgressDisplayManager _progressDisplayManager;
  private readonly IOperations _operations;
  private readonly IClientFactory _clientFactory;
  private readonly ISdkActivityFactory _activityFactory;

  public RootObjectSender(
    ISendConversionCache sendConversionCache,
    AccountService accountService,
    IProgressDisplayManager progressDisplayManager,
    IOperations operations,
    IClientFactory clientFactory,
    ISdkActivityFactory activityFactory,
    IServerTransportFactory transportFactory
  )
  {
    _sendConversionCache = sendConversionCache;
    _accountService = accountService;
    _progressDisplayManager = progressDisplayManager;
    _operations = operations;
    _clientFactory = clientFactory;
    _activityFactory = activityFactory;
    _transportFactory = transportFactory;
  }

  /// <summary>
  /// Contract for the send operation that handles an assembled <see cref="Base"/> object.
  /// In production, this will send to a server.
  /// In testing, this could send to a sqlite db or just save to a dictionary.
  /// </summary>
  public async Task<SerializeProcessResults> Send(
    Base commitObject,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Uploading...", null));

    Account account = _accountService.GetAccountWithServerUrlFallback(sendInfo.AccountId, sendInfo.ServerUrl);
    using var userScope = ActivityScope.SetTag(Consts.USER_ID, account.GetHashedEmail());
    using var activity = _activityFactory.Start("SendOperation");

    string previousSpeed = string.Empty;
    using var transport = _transportFactory.Create(account, sendInfo.ProjectId, 60, null);
    _progressDisplayManager.Begin();
    var send1Results = await _operations
      .Send(
        commitObject,
        transport,
        true,
        onProgressAction: new PassthroughProgress(args =>
        {
          if (args.ProgressEvent == ProgressEvent.UploadBytes)
          {
            switch (args.ProgressEvent)
            {
              case ProgressEvent.UploadBytes:
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
            case ProgressEvent.CachedToLocal:
              onOperationProgressed.Report(new($"Caching... ({args.Count})", null));
              break;
            case ProgressEvent.UploadBytes:
              onOperationProgressed.Report(new($"Uploading... ({previousSpeed}) {args.Count}", null));
              break;
            case ProgressEvent.FromCacheOrSerialized:
              onOperationProgressed.Report(
                new(
                  $"Loading cache and Serializing... ({_progressDisplayManager.CalculateSpeed(args)})",
                  _progressDisplayManager.CalculatePercentage(args)
                )
              );
              break;
          }
        }),
        ct
      )
      .ConfigureAwait(false);

    var sendResult = new SerializeProcessResults(
      send1Results.rootObjId,
      send1Results.convertedReferences.ToDictionary(x => new Id(x.Key), x => x.Value)
    );
    _sendConversionCache.StoreSendResult(sendInfo.ProjectId, sendResult.ConvertedReferences);

    ct.ThrowIfCancellationRequested();

    onOperationProgressed.Report(new("Linking version to model...", null));

    // 8 - Create the version (commit)
    using var apiClient = _clientFactory.Create(account);
    _ = await apiClient
      .Version.Create(
        new CreateVersionInput(
          sendResult.RootId,
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
