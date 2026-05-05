using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.MicroStation.Plugin;
using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Persists Speckle model card state as a JSON file in the user's AppData folder.
/// The file is keyed by a SHA-256 hash of the active DGN file's full path so that
/// each DGN file has its own independent model card state.
/// </summary>
public sealed class MicroStationDocumentModelStore : DocumentModelStore
{
  private static readonly string s_stateDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Speckle",
    "MicroStation"
  );

  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private string _lastSavedState = string.Empty;

  public MicroStationDocumentModelStore(
    ILogger<DocumentModelStore> logger,
    IJsonSerializer jsonSerializer,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base(logger, jsonSerializer)
  {
    _topLevelExceptionHandler = topLevelExceptionHandler;
    LoadState();
  }

  public void ReloadState() => LoadState();

  protected override void LoadState()
  {
    var path = GetStateFilePath();
    if (string.IsNullOrEmpty(path) || !File.Exists(path))
    {
      ClearAndSave();
      return;
    }

    try
    {
      var json = File.ReadAllText(path, Encoding.UTF8);
      LoadFromString(json);
      _lastSavedState = json;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      ClearAndSave();
      _topLevelExceptionHandler.CatchUnhandled(() =>
        throw new InvalidOperationException("Failed to read Speckle state from file.", ex)
      );
    }
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    var path = GetStateFilePath();
    if (string.IsNullOrEmpty(path))
    {
      return;
    }

    if (modelCardState == _lastSavedState)
    {
      return;
    }

    try
    {
      Directory.CreateDirectory(s_stateDir);
      File.WriteAllText(path, modelCardState, Encoding.UTF8);
      _lastSavedState = modelCardState;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _topLevelExceptionHandler.CatchUnhandled(() =>
        throw new InvalidOperationException("Failed to write Speckle state to file.", ex)
      );
    }
  }

  private static string GetStateFilePath()
  {
    var app = MsApp.TryGetInstance();
    if (app?.HasActiveDesignFile != true)
    {
      return string.Empty;
    }

    var fullName = app.ActiveDesignFile.FullName;
    if (string.IsNullOrEmpty(fullName))
    {
      return string.Empty;
    }

    using var sha = SHA256.Create();
    var hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(fullName)))
      .Replace('/', '_')
      .Replace('+', '-')
      .TrimEnd('=');

    return Path.Combine(s_stateDir, $"{hash}.json");
  }
}
