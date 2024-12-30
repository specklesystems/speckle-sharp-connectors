using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.RhinoShared;
using Speckle.Sdk;
using Speckle.Sdk.Helpers;
using Speckle.Sdk.SQLite;

namespace Speckle.Connectors.TeklaShared.HostApp;

public class TeklaDocumentModelStore : DocumentModelStore
{
  private readonly ILogger<TeklaDocumentModelStore> _logger;
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
    _jsonCacheManager = jsonCacheManagerFactory.CreateForUser("ConnectorsFileData");
    _model = new TSM.Model();
    GenerateKey();
    eventAggregator
      .GetEvent<ModelLoad>()
      .Publish(() =>
      {
        GenerateKey();
        LoadState();
        eventAggregator.GetEvent<DocumentChangedEvent>().Publish(new object());
      });
    if (SpeckleTeklaPanelHost.IsInitialized)
    {
      LoadState();
      eventAggregator.GetEvent<DocumentChangedEvent>().Publish(new object());
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
