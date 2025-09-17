using System.IO;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Logging;
using Timer = System.Timers.Timer;

namespace Speckle.Connectors.CSiShared.HostApp;

public class CsiDocumentModelStore : DocumentModelStore, IDisposable
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ILogger<CsiDocumentModelStore> _logger;
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IThreadContext _threadContext;
  private readonly Timer _modelCheckTimer;
  private string _lastModelFilename = string.Empty;
  private bool _disposed;
  private string HostAppUserDataPath { get; set; }
  private string DocumentStateFile { get; set; }
  private string ModelPathHash { get; set; }

  public CsiDocumentModelStore(
    ILogger<DocumentModelStore> baseLogger,
    IJsonSerializer jsonSerializer,
    ISpeckleApplication speckleApplication,
    ILogger<CsiDocumentModelStore> logger,
    ICsiApplicationService csiApplicationService,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IThreadContext threadContext
  )
    : base(baseLogger, jsonSerializer)
  {
    _threadContext = threadContext;
    _speckleApplication = speckleApplication;
    _logger = logger;
    _csiApplicationService = csiApplicationService;
    _topLevelExceptionHandler = topLevelExceptionHandler;

    // initialize timer to check for model changes
    _modelCheckTimer = new Timer(1000);

    // timer runs on background thread but model checks must be on main thread
    _modelCheckTimer.Elapsed += (_, _) =>
      _topLevelExceptionHandler.CatchUnhandled(() => _threadContext.RunOnMain(CheckModelChanges));
    _modelCheckTimer.Start();
  }

  private void CheckModelChanges()
  {
    string currentFilename = _csiApplicationService.SapModel.GetModelFilename();

    if (string.IsNullOrEmpty(currentFilename) || currentFilename == _lastModelFilename)
    {
      return;
    }

    _lastModelFilename = currentFilename;
    SetPaths();
    LoadState();
    OnDocumentChanged();
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
      ModelPathHash = Md5.GetString(_csiApplicationService.SapModel.GetModelFilename());
      HostAppUserDataPath = Path.Combine(
        SpecklePathProvider.UserSpeckleFolderPath,
        "ConnectorsFileData",
        _speckleApplication.Slug
      );
      DocumentStateFile = Path.Combine(HostAppUserDataPath, $"{ModelPathHash}.json");
      _logger.LogDebug("Paths set - Hash: {ModelPathHash}, File: {DocumentStateFile}", ModelPathHash, DocumentStateFile);
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
