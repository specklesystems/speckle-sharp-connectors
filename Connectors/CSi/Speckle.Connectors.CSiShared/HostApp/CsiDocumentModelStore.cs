using System.IO;
using System.Timers;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Helpers;
using Speckle.Sdk.Logging;
using Timer = System.Timers.Timer;

namespace Speckle.Connectors.CSiShared.HostApp;

public class CsiDocumentModelStore : DocumentModelStore, IDisposable
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ILogger<CsiDocumentModelStore> _logger;
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly Timer _modelCheckTimer;
  private readonly IEventAggregator _eventAggregator;
  private string _lastModelFilename = string.Empty;
  private bool _disposed;
  private string HostAppUserDataPath { get; set; }
  private string DocumentStateFile { get; set; }
  private string ModelPathHash { get; set; }

  public CsiDocumentModelStore(
    IJsonSerializer jsonSerializer,
    ISpeckleApplication speckleApplication,
    ILogger<CsiDocumentModelStore> logger,
    ICsiApplicationService csiApplicationService,
    IEventAggregator eventAggregator
  )
    : base(jsonSerializer)
  {
    _speckleApplication = speckleApplication;
    _logger = logger;
    _csiApplicationService = csiApplicationService;
    _eventAggregator = eventAggregator;

    // initialize timer to check for model changes
    _modelCheckTimer = new Timer(1000);
    _modelCheckTimer.Elapsed += CheckModelChanges;
    _modelCheckTimer.Start();
  }

  private async void CheckModelChanges(object? source, ElapsedEventArgs e)
  {
    string currentFilename = _csiApplicationService.SapModel.GetModelFilename();

    if (string.IsNullOrEmpty(currentFilename) || currentFilename == _lastModelFilename)
    {
      return;
    }

    _lastModelFilename = currentFilename;
    SetPaths();
    LoadState();

    await _eventAggregator.GetEvent<DocumentStoreChangedEvent>().PublishAsync(new object());
  }

  public override Task OnDocumentStoreInitialized()
  {
    var currentFilename = _csiApplicationService.SapModel.GetModelFilename();
    if (!string.IsNullOrEmpty(currentFilename))
    {
      _lastModelFilename = currentFilename;
      SetPaths();
      LoadState();
    }
    return Task.CompletedTask;
  }

  private void SetPaths()
  {
    try
    {
      ModelPathHash = Crypt.Md5(_csiApplicationService.SapModel.GetModelFilename(), length: 32);
      HostAppUserDataPath = Path.Combine(
        SpecklePathProvider.UserSpeckleFolderPath,
        "ConnectorsFileData",
        _speckleApplication.Slug
      );
      DocumentStateFile = Path.Combine(HostAppUserDataPath, $"{ModelPathHash}.json");
      _logger.LogDebug($"Paths set - Hash: {ModelPathHash}, File: {DocumentStateFile}");
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Error in setting paths for CsiDocumentModelStore");
    }
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    try
    {
      if (!Directory.Exists(HostAppUserDataPath))
      {
        Directory.CreateDirectory(HostAppUserDataPath);
      }

      File.WriteAllText(DocumentStateFile, modelCardState);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to save state");
    }
  }

  protected override void LoadState()
  {
    try
    {
      if (!File.Exists(DocumentStateFile))
      {
        ClearAndSave();
        return;
      }

      string serializedState = File.ReadAllText(DocumentStateFile);
      LoadFromString(serializedState);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to load state, initializing empty state");
      ClearAndSave();
    }
  }

  protected virtual void Dispose(bool disposing)
  {
    if (_disposed)
    {
      return;
    }

    if (disposing)
    {
      _modelCheckTimer.Dispose();
    }

    _disposed = true;
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }
}
