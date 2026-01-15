using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksBasicConnectorBinding(
  IBrowserBridge parent,
  DocumentModelStore store,
  ISpeckleApplication speckleApplication
) : IBasicConnectorBinding
{
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; } = parent;

  public BasicConnectorBindingCommands Commands { get; } = new(parent);

  public string GetSourceApplicationName() => speckleApplication.Slug;

  public string GetSourceApplicationVersion() => speckleApplication.HostApplicationVersion;

  public string GetConnectorVersion() => speckleApplication.SpeckleVersion;

  public DocumentInfo? GetDocumentInfo() =>
    NavisworksApp.ActiveDocument is null || NavisworksApp.ActiveDocument.Models.Count == 0
      ? null
      : new DocumentInfo(
        NavisworksApp.ActiveDocument.CurrentFileName,
        NavisworksApp.ActiveDocument.Title,
        NavisworksApp.ActiveDocument.GetHashCode().ToString()
      );

  public DocumentModelStore GetDocumentState() => store;

  public void AddModel(ModelCard model) => store.AddModel(model);

  public void UpdateModel(ModelCard model) => store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => store.RemoveModel(model);

  public void RemoveModels(List<ModelCard> models) => store.RemoveModels(models);

  public Task HighlightModel(string modelCardId) => Task.CompletedTask;

  public async Task HighlightObjects(IReadOnlyList<string> objectIds) =>
    // TODO: Implement highlighting logic on main thread
    await Task.CompletedTask;
}
