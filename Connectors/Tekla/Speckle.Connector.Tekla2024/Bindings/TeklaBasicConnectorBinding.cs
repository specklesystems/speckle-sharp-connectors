﻿using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;

namespace Speckle.Connector.Tekla2024.Bindings;

public class TeklaBasicConnectorBinding : IBasicConnectorBinding
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly DocumentModelStore _store;
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; }

  public TeklaBasicConnectorBinding(
    IBrowserBridge parent,
    ISpeckleApplication speckleApplication,
    DocumentModelStore store
  )
  {
    _speckleApplication = speckleApplication;
    _store = store;
    Parent = parent;
  }

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public DocumentInfo? GetDocumentInfo() => new DocumentInfo("Test", "Test", "Test");

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => throw new NotImplementedException();

  public void UpdateModel(ModelCard model) => throw new NotImplementedException();

  public void RemoveModel(ModelCard model) => throw new NotImplementedException();

  public void HighlightModel(string modelCardId) => throw new NotImplementedException();

  public void HighlightObjects(List<string> objectIds) => throw new NotImplementedException();

  public BasicConnectorBindingCommands Commands { get; }
}