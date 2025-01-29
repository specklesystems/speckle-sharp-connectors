using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Helpers;
using Speckle.Sdk.SQLite;

namespace Speckle.Connectors.TeklaShared.HostApp;

public class TeklaDocumentModelStore : DocumentModelStore
{
  private readonly ILogger<TeklaDocumentModelStore> _logger;
  private readonly IEventAggregator _eventAggregator;
  private readonly ISqLiteJsonCacheManager _jsonCacheManager;
  private readonly TSM.Model _model;
  private string? _modelKey;

  public TeklaDocumentModelStore(
    IJsonSerializer jsonSerializer,
    ILogger<TeklaDocumentModelStore> logger,
    ISqLiteJsonCacheManagerFactory jsonCacheManagerFactory,
    IEventAggregator eventAggregator
  )
    : base(jsonSerializer)
  {
    _logger = logger;
    _eventAggregator = eventAggregator;
    _jsonCacheManager = jsonCacheManagerFactory.CreateForUser("ConnectorsFileData");
    _model = new TSM.Model();
    GenerateKey();
    eventAggregator.GetEvent<ModelLoadEvent>().Subscribe(OnModelLoadEvent);
  }

  private async Task OnModelLoadEvent(object _)
  {
    GenerateKey();
    LoadState();
    await _eventAggregator.GetEvent<DocumentStoreChangedEvent>().PublishAsync(new object());
  }

  public override async Task OnDocumentStoreInitialized()
  {
    if (SpeckleTeklaPanelHost.IsInitialized)
    {
      LoadState();
      await _eventAggregator.GetEvent<DocumentStoreChangedEvent>().PublishAsync(new object());
    }
  }

  private void GenerateKey() => _modelKey = Crypt.Md5(_model.GetInfo().ModelPath, length: 32);

  protected override void HostAppSaveState(string modelCardState)
  {
    try
    {
      if (_modelKey is null)
      {
        return;
      }
      _jsonCacheManager.UpdateObject(_modelKey, modelCardState);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex.Message);
    }
  }

  protected override void LoadState()
  {
    if (_modelKey is null)
    {
      return;
    }
    var state = _jsonCacheManager.GetObject(_modelKey);
    LoadFromString(state);
  }
}
