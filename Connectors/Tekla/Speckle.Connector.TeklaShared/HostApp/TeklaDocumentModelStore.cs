using System.IO;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.SQLite;

namespace Speckle.Connector.Tekla2024.HostApp;

public class TeklaDocumentModelStore : DocumentModelStore
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ILogger<TeklaDocumentModelStore> _logger;
  private readonly ISqLiteJsonCacheManager _jsonCacheManager;
  private readonly TSM.Events _events;

  public TeklaDocumentModelStore(
    IJsonSerializer jsonSerializer,
    ISpeckleApplication speckleApplication,
    ILogger<TeklaDocumentModelStore> logger,
    ISqLiteJsonCacheManagerFactory jsonCacheManagerFactory
  )
    : base(jsonSerializer)
  {
    _speckleApplication = speckleApplication;
    _logger = logger;
    _jsonCacheManager = jsonCacheManagerFactory.CreateForUser(
      Path.Combine("ConnectorsFileData", _speckleApplication.Slug)
    );
    _events = new TSM.Events();
    _events.ModelLoad += () =>
    {
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

  private string GetKey() => _speckleApplication.ApplicationAndVersion;

  protected override void HostAppSaveState(string modelCardState)
  {
    try
    {
      _jsonCacheManager.SaveObject(GetKey(), modelCardState);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex.Message);
    }
  }

  protected override void LoadState()
  {
    var state = _jsonCacheManager.GetObject(GetKey());
    LoadFromString(state);
  }
}
