using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;

namespace Speckle.Connectors.CSiShared.Bindings;

public class CsiSharedBasicConnectorBinding : IBasicConnectorBinding
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly DocumentModelStore _store;
  private readonly IEventAggregator _eventAggregator;
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; }
  public BasicConnectorBindingCommands Commands { get; }

  public CsiSharedBasicConnectorBinding(
    IBrowserBridge parent,
    ISpeckleApplication speckleApplication,
    DocumentModelStore store,
    IEventAggregator eventAggregator
  )
  {
    Parent = parent;
    _speckleApplication = speckleApplication;
    _store = store;
    Commands = new BasicConnectorBindingCommands(Parent);
    _eventAggregator = eventAggregator;

    _eventAggregator.GetEvent<DocumentStoreChangedEvent>().Subscribe(OnDocumentStoreChangedEvent);
  }

  private async Task OnDocumentStoreChangedEvent(object _) => await Commands.NotifyDocumentChanged();

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public DocumentInfo? GetDocumentInfo() => new("ETABS Model", "ETABS Model", "1");

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.AddModel(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public Task HighlightModel(string modelCardId) => Task.CompletedTask;

  public Task HighlightObjects(IReadOnlyList<string> objectIds) => Task.CompletedTask;
}
