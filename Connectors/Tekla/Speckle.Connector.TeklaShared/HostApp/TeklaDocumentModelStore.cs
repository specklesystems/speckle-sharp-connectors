using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Helpers;
using Speckle.Sdk.SQLite;

namespace Speckle.Connectors.TeklaShared.HostApp;

public class TeklaDocumentModelStore : DocumentModelStore
{
  private readonly ILogger<TeklaDocumentModelStore> _logger;
  private readonly ISqLiteJsonCacheManager _jsonCacheManager;
  private readonly TSM.Events _events;
  private readonly TSM.Model _model;
  private string? _modelKey;

  public TeklaDocumentModelStore(
    IJsonSerializer jsonSerializer,
    ILogger<TeklaDocumentModelStore> logger,
    ISqLiteJsonCacheManagerFactory jsonCacheManagerFactory
  )
    : base(jsonSerializer)
  {
    _logger = logger;
    _jsonCacheManager = jsonCacheManagerFactory.CreateForUser("ConnectorsFileData");
    _events = new TSM.Events();
    _model = new TSM.Model();
    GenerateKey();
    _events.ModelLoad += () =>
    {
      GenerateKey();
      LoadState();
      OnDocumentChanged();
    };
    _events.Register();
    if (SpeckleTeklaPanelHost.IsInitialized)
    {
      LoadState();
      OnDocumentChanged();
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
      _jsonCacheManager.SaveObject(_modelKey, modelCardState);
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
