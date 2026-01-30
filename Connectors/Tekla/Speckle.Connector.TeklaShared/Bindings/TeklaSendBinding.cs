using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.TeklaShared.Operations.Send.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.TeklaShared;
using Speckle.Sdk.Common;
using Tekla.Structures;
using Tekla.Structures.Model;
using Task = System.Threading.Tasks.Task;

namespace Speckle.Connectors.TeklaShared.Bindings;

public sealed class TeklaSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  public IBrowserBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly List<ISendFilter> _sendFilters;
  private readonly ICancellationManager _cancellationManager;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ILogger<TeklaSendBinding> _logger;
  private readonly ITeklaConversionSettingsFactory _teklaConversionSettingsFactory;
  private readonly Model _model;
  private readonly ToSpeckleSettingsManager _toSpeckleSettingsManager;
  private readonly Events _events;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;

  private ConcurrentDictionary<string, byte> ChangedObjectIds { get; set; } = new();

  public TeklaSendBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    ICancellationManager cancellationManager,
    ISendConversionCache sendConversionCache,
    ILogger<TeklaSendBinding> logger,
    ITeklaConversionSettingsFactory teklaConversionSettingsFactory,
    ToSpeckleSettingsManager toSpeckleSettingsManager,
    ISendOperationManagerFactory sendOperationManagerFactory
  )
  {
    _store = store;
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _sendConversionCache = sendConversionCache;
    _logger = logger;
    _teklaConversionSettingsFactory = teklaConversionSettingsFactory;
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _toSpeckleSettingsManager = toSpeckleSettingsManager;
    _sendOperationManagerFactory = sendOperationManagerFactory;

    _model = new Model();
    _events = new Events();
    SubscribeToTeklaEvents();
  }

  private void SubscribeToTeklaEvents()
  {
    _events.ModelObjectChanged += OnModelObjectChanged;
    _events.Register();
  }

  // subscribes the all changes in a modelobject
  private void OnModelObjectChanged(List<ChangeData> changes)
  {
    foreach (var change in changes)
    {
      if (change.Object is { } modelObj)
      {
        ChangedObjectIds[modelObj.Identifier.GUID.ToString()] = 1;
      }
    }

    if (changes.Count > 0)
    {
      // directly calling the RunExpirationChecks, not triggering the idle
      // TODO: Figure out how idleing works in Tekla
      Task.FromResult(RunExpirationChecks());
    }
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [new SendRebarsAsSolidSetting(false)];

  public async Task Send(string modelCardId)
  {
    var (fileName, fileSizeBytes) = GetFileInfo();
    using var manager = _sendOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      (sp, card) =>
        sp.GetRequiredService<IConverterSettingsStore<TeklaConversionSettings>>()
          .Initialize(
            _teklaConversionSettingsFactory.Create(_model, _toSpeckleSettingsManager.GetSendRebarsAsSolid(card))
          ),
      card =>
        card.SendFilter.NotNull()
          .RefreshObjectIds()
          .Select(id => _model.SelectModelObject(new Identifier(new Guid(id))))
          .Where(obj => obj != null)
          .ToList(),
      fileName,
      fileSizeBytes
    );
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  private (string? fileName, long? fileSizeBytes) GetFileInfo()
  {
    string? path = _model.GetInfo()?.ModelPath;
    if (path is null || !File.Exists(path))
    {
      return (null, null);
    }
    FileInfo file = new(path);
    return (file.Name, file.Length);
  }

  private async Task RunExpirationChecks()
  {
    if (!_model.GetConnectionStatus())
    {
      _logger.LogError("Tekla expiration checks were running without an active model.");
      return;
    }

    var senders = _store.GetSenders();
    string[] objectIdsList = ChangedObjectIds.Keys.ToArray();
    List<string> expiredSenderIds = new();

    _sendConversionCache.EvictObjects(objectIdsList);

    foreach (SenderModelCard modelCard in senders)
    {
      var intersection = modelCard.SendFilter.NotNull().SelectedObjectIds.Intersect(objectIdsList).ToList();
      var isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    await Commands.SetModelsExpired(expiredSenderIds);

    ChangedObjectIds = new ConcurrentDictionary<string, byte>();
  }
}
