using System.IO;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Helpers;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.CSiShared.HostApp;

public class CsiDocumentModelStore(
  IJsonSerializer jsonSerializerSettings,
  ISpeckleApplication speckleApplication,
  ILogger<CsiDocumentModelStore> logger,
  ICsiApplicationService csiApplicationService
) : DocumentModelStore(jsonSerializerSettings)
{
  private string HostAppUserDataPath { get; set; }
  private string DocumentStateFile { get; set; }
  private string ModelPathHash { get; set; }

  public override Task OnDocumentStoreInitialized()
  {
    SetPaths();
    LoadState();
    return Task.CompletedTask;
  }

  private void SetPaths()
  {
    ModelPathHash = Crypt.Md5(csiApplicationService.SapModel.GetModelFilepath(), length: 32);
    HostAppUserDataPath = Path.Combine(
      SpecklePathProvider.UserSpeckleFolderPath,
      "ConnectorsFileData",
      speckleApplication.Slug
    );
    DocumentStateFile = Path.Combine(HostAppUserDataPath, $"{ModelPathHash}.json");
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
      logger.LogError(ex.Message);
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
