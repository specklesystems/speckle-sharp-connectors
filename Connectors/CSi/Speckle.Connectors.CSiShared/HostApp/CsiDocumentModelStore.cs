using System.IO;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Helpers;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.CSiShared.HostApp;

public class CsiDocumentModelStore : DocumentModelStore
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ILogger<CsiDocumentModelStore> _logger;
  private readonly ICsiApplicationService _csiApplicationService;
  private string HostAppUserDataPath { get; set; }
  private string DocumentStateFile { get; set; }
  private string ModelPathHash { get; set; }

  public CsiDocumentModelStore(
    IJsonSerializer jsonSerializer,
    ISpeckleApplication speckleApplication,
    ILogger<CsiDocumentModelStore> logger,
    ICsiApplicationService csiApplicationService
  )
    : base(jsonSerializer)
  {
    _speckleApplication = speckleApplication;
    _logger = logger;
    _csiApplicationService = csiApplicationService;
  }

  public override Task OnDocumentStoreInitialized()
  {
    SetPaths();
    LoadState();
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
      _logger.LogError(ex.Message);
    }
  }

  protected override void LoadState()
  {
    if (!Directory.Exists(HostAppUserDataPath))
    {
      ClearAndSave();
      return;
    }

    if (!File.Exists(DocumentStateFile))
    {
      ClearAndSave();
      return;
    }

    string serializedState = File.ReadAllText(DocumentStateFile);
    LoadFromString(serializedState);
  }
}
