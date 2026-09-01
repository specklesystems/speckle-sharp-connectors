using System.IO;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.MicroStation.Plugin;
using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.Bindings;

public class MicroStationBasicConnectorBinding(
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

  public DocumentInfo? GetDocumentInfo()
  {
    var app = MsApp.TryGetInstance();
    if (app?.HasActiveDesignFile != true)
    {
      return null;
    }

    var fullName = app.ActiveDesignFile.FullName;
    var title = Path.GetFileNameWithoutExtension(fullName);
    return new DocumentInfo(fullName, title, fullName.GetHashCode().ToString());
  }

  public DocumentModelStore GetDocumentState() => store;

  public void AddModel(ModelCard model) => store.AddModel(model);

  public void UpdateModel(ModelCard model) => store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => store.RemoveModel(model);

  public void RemoveModels(List<ModelCard> models) => store.RemoveModels(models);

  public Task HighlightModel(string modelCardId) => Task.CompletedTask;

  public Task HighlightObjects(IReadOnlyList<string> objectIds) => Task.CompletedTask;
}
