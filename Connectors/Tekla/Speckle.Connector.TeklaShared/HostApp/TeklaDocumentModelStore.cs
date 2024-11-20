﻿using System.IO;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk;
using Speckle.Sdk.Helpers;
using Speckle.Sdk.Logging;

namespace Speckle.Connector.Tekla2024.HostApp;

public class TeklaDocumentModelStore : DocumentModelStore
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ILogger<TeklaDocumentModelStore> _logger;
  private readonly TSM.Model _model;
  private readonly TSM.Events _events;
  private string HostAppUserDataPath { get; set; }
  private string DocumentStateFile { get; set; }
  private string ModelPathHash { get; set; }

  public TeklaDocumentModelStore(
    JsonSerializerSettings jsonSerializerSettings,
    ISpeckleApplication speckleApplication,
    ILogger<TeklaDocumentModelStore> logger
  )
    : base(jsonSerializerSettings)
  {
    _speckleApplication = speckleApplication;
    _logger = logger;
    _model = new TSM.Model();
    SetPaths();
    _events = new TSM.Events();
    _events.ModelLoad += () =>
    {
      SetPaths();
      ReadFromFile();
      OnDocumentChanged();
    };
    _events.Register();
    if (SpeckleTeklaPanelHost.IsInitialized)
    {
      ReadFromFile();
      OnDocumentChanged();
    }
  }

  private void SetPaths()
  {
    ModelPathHash = Crypt.Md5(_model.GetInfo().ModelPath, length: 32);
    HostAppUserDataPath = Path.Combine(
      SpecklePathProvider.UserSpeckleFolderPath,
      "Connectors",
      _speckleApplication.Slug
    );
    DocumentStateFile = Path.Combine(HostAppUserDataPath, $"{ModelPathHash}.json");
  }

  public override void WriteToFile()
  {
    string serializedState = Serialize();
    try
    {
      if (!Directory.Exists(HostAppUserDataPath))
      {
        Directory.CreateDirectory(HostAppUserDataPath);
      }
      File.WriteAllText(DocumentStateFile, serializedState);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex.Message);
    }
  }

  public override void ReadFromFile()
  {
    if (!Directory.Exists(HostAppUserDataPath))
    {
      Clear();
      return;
    }

    if (!File.Exists(DocumentStateFile))
    {
      Clear();
      return;
    }

    string serializedState = File.ReadAllText(DocumentStateFile);
    LoadFromString(serializedState);
  }
}
