using System.IO;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.TSDShared.HostApp;

/// <summary>
/// Phase 0 store: TSD has no document open yet (we only boot DUI), so model cards are persisted to a single
/// per-slug file under the Speckle user folder. Once we connect to a running TSD model (Phase 1) this should key
/// the state file off the model path, like <c>CsiDocumentModelStore</c> does.
/// </summary>
internal sealed class TSDDocumentModelStore : DocumentModelStore
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ILogger<TSDDocumentModelStore> _logger;
  private string DocumentStateFile { get; }
  private string HostAppUserDataPath { get; }

  public TSDDocumentModelStore(
    ILogger<DocumentModelStore> baseLogger,
    IJsonSerializer jsonSerializer,
    ISpeckleApplication speckleApplication,
    ILogger<TSDDocumentModelStore> logger
  )
    : base(baseLogger, jsonSerializer)
  {
    _speckleApplication = speckleApplication;
    _logger = logger;

    HostAppUserDataPath = Path.Combine(
      SpecklePathProvider.UserSpeckleFolderPath,
      "ConnectorsFileData",
      _speckleApplication.Slug
    );
    DocumentStateFile = Path.Combine(HostAppUserDataPath, "document.json");

    LoadState();
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
      _logger.LogError(ex, "Failed to save TSD document state");
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
      _logger.LogError(ex, "Failed to load TSD document state, initializing empty state");
      ClearAndSave();
    }
  }
}
