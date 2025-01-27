using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;

namespace Speckle.Connectors.CSiShared.Bindings;

public class CsiSharedBasicConnectorBinding(
  IBrowserBridge parent,
  ISpeckleApplication speckleApplication,
  DocumentModelStore store
) : IBasicConnectorBinding
{
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; } = parent;
  public BasicConnectorBindingCommands Commands { get; } = new(parent);

  public string GetConnectorVersion() => speckleApplication.SpeckleVersion;

  public string GetSourceApplicationName() => speckleApplication.Slug;

  public string GetSourceApplicationVersion() => speckleApplication.HostApplicationVersion;

  public DocumentInfo? GetDocumentInfo() => new("ETABS Model", "ETABS Model", "1");

  public DocumentModelStore GetDocumentState() => store;

  public void AddModel(ModelCard model) => store.AddModel(model);

  public void UpdateModel(ModelCard model) => store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => store.RemoveModel(model);

  public Task HighlightModel(string modelCardId) => Task.CompletedTask;

  public Task HighlightObjects(IReadOnlyList<string> objectIds) => Task.CompletedTask;
}
