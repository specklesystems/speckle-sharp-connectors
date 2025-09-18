using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.SQLite;

namespace Speckle.Connectors.TeklaShared.HostApp;

public class TeklaDocumentModelStore : DocumentModelStore
{
  private readonly ILogger<TeklaDocumentModelStore> _logger;
  private readonly ISqLiteJsonCacheManager _jsonCacheManager;
  private readonly TSM.Model _model;
  private string? _modelKey;
  private readonly TSM.Events _events;

  public TeklaDocumentModelStore(
    ILogger<DocumentModelStore> baseLogger,
    IJsonSerializer jsonSerializer,
    ILogger<TeklaDocumentModelStore> logger,
    ISqLiteJsonCacheManagerFactory jsonCacheManagerFactory
  )
    : base(baseLogger, jsonSerializer)
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

  private void GenerateKey() => _modelKey = Md5.GetString(_model.GetInfo().ModelPath);

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
      _logger.LogError(ex, "Failed to Save Host App State");
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
