using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connectors.Common.Caching;
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
  public BasicConnectorBindingCommands Commands { get; }

  private readonly DocumentModelStore _store;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly NavisworksDocumentEvents _documentEvents;

  public NavisworksBasicConnectorBinding(
    IBrowserBridge parent,
    DocumentModelStore store,
    ISendConversionCache sendConversionCache,
    ISpeckleApplication speckleApplication,
    NavisworksDocumentEvents documentEvents
  )
  {
    Parent = parent;
    _store = store;
    _sendConversionCache = sendConversionCache;
    _speckleApplication = speckleApplication;
    _documentEvents = documentEvents;
    Commands = new BasicConnectorBindingCommands(parent);
  }

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public DocumentInfo? GetDocumentInfo() 
  =>
            NavisworksApp.ActiveDocument is null || NavisworksApp.ActiveDocument.Models.Count == 0
              ? null
              : new DocumentInfo(
                NavisworksApp.ActiveDocument.CurrentFileName,
                NavisworksApp.ActiveDocument.Title,
                NavisworksApp.ActiveDocument.GetHashCode().ToString()
              )
          
      ;

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.AddModel(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public Task HighlightModel(string modelCardId) => Task.CompletedTask;

  public async Task HighlightObjects(IReadOnlyList<string> objectIds) =>
        // TODO: Implement highlighting logic on main thread
        await Task.CompletedTask.ConfigureAwait(false);
}
