using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.TSDShared.HostApp;
using Speckle.Sdk;

namespace Speckle.Connectors.TSDShared.Bindings;

internal sealed class TSDBasicConnectorBinding : IBasicConnectorBinding
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly DocumentModelStore _store;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IThreadContext _threadContext;
  private readonly ITSDApplicationService _applicationService;

  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; }
  public BasicConnectorBindingCommands Commands { get; }

  public TSDBasicConnectorBinding(
    IBrowserBridge parent,
    ISpeckleApplication speckleApplication,
    DocumentModelStore store,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IThreadContext threadContext,
    ITSDApplicationService applicationService
  )
  {
    Parent = parent;
    _speckleApplication = speckleApplication;
    _store = store;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _threadContext = threadContext;
    _applicationService = applicationService;
    Commands = new BasicConnectorBindingCommands(Parent);

    _store.DocumentChanged += (_, _) =>
      _topLevelExceptionHandler.FireAndForget(async () =>
      {
        await _threadContext.RunOnMainAsync(async () =>
        {
          await Commands.NotifyDocumentChanged();
        });
      });
  }

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public DocumentInfo? GetDocumentInfo() =>
    _applicationService.IsConnected
      ? new DocumentInfo(
        _applicationService.ApplicationTitle ?? "Tekla Structural Designer",
        _applicationService.ApplicationTitle ?? "TSD Model",
        "1"
      )
      : null;

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _topLevelExceptionHandler.CatchUnhandled(() => _store.AddModel(model));

  public void UpdateModel(ModelCard model) => _topLevelExceptionHandler.CatchUnhandled(() => _store.UpdateModel(model));

  public void RemoveModel(ModelCard model) => _topLevelExceptionHandler.CatchUnhandled(() => _store.RemoveModel(model));

  public void RemoveModels(List<ModelCard> models) =>
    _topLevelExceptionHandler.CatchUnhandled(() => _store.RemoveModels(models));

  public Task HighlightModel(string modelCardId) => Task.CompletedTask;

  public Task HighlightObjects(IReadOnlyList<string> objectIds) => Task.CompletedTask;
}
