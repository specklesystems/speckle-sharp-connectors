using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksBasicConnectorBinding : IBasicConnectorBinding
{
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; }

  public NavisworksBasicConnectorBinding(IBrowserBridge parent)
  {
    Parent = parent;
  }

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public DocumentInfo? GetDocumentInfo() =>
    NavisworksApp.ActiveDocument is null || NavisworksApp.ActiveDocument.Models.Count == 0
      ? null
      : new DocumentInfo(
        NavisworksApp.ActiveDocument.CurrentFileName,
        NavisworksApp.ActiveDocument.Title,
        NavisworksApp.ActiveDocument.GetHashCode().ToString()
      );

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.Models.Add(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public Task HighlightModel(string modelCardId)
  {
    return Task.CompletedTask;
  }

  public Task HighlightObjects(IReadOnlyList<string> objectIds)
  {
    return Task.CompletedTask;
  }

  public BasicConnectorBindingCommands Commands { get; }

  private readonly ISpeckleApplication _speckleApplication;
  private readonly DocumentModelStore _store;

  public NavisworksBasicConnectorBinding(
    IBrowserBridge parent,
    ISpeckleApplication speckleApplication,
    DocumentModelStore store
  )
  {
    Parent = parent;
    Commands = new BasicConnectorBindingCommands(parent);
    _speckleApplication = speckleApplication;
    _store = store;
  }
}
