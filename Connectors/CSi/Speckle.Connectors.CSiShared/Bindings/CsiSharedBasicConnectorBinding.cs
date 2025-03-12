using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;

namespace Speckle.Connectors.CSiShared.Bindings;

public class CsiSharedBasicConnectorBinding : IBasicConnectorBinding
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly DocumentModelStore _store;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IThreadContext _threadContext;
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; }
  public BasicConnectorBindingCommands Commands { get; }

  public CsiSharedBasicConnectorBinding(
    IBrowserBridge parent,
    ISpeckleApplication speckleApplication,
    DocumentModelStore store,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IThreadContext threadContext
  )
  {
    _threadContext = threadContext;
    Parent = parent;
    _speckleApplication = speckleApplication;
    _store = store;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    Commands = new BasicConnectorBindingCommands(Parent);

    _store.DocumentChanged += (_, _) =>
      _topLevelExceptionHandler.FireAndForget(async () =>
      {
        // enforce main thread
        await _threadContext.RunOnMainAsync(async () =>
        {
          await Commands.NotifyDocumentChanged();
        });
      });
  }

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public DocumentInfo? GetDocumentInfo() => new("ETABS Model", "ETABS Model", "1");

  public DocumentModelStore GetDocumentState() => _store;

  /// <remarks>Operations must run on the main thread for ETABS and SAP 2000</remarks>
  public void AddModel(ModelCard model) =>
    _topLevelExceptionHandler.CatchUnhandled(() => _threadContext.RunOnThread(() => _store.AddModel(model), true));

  /// <remarks>Operations must run on the main thread for ETABS and SAP 2000</remarks>
  public void UpdateModel(ModelCard model) =>
    _topLevelExceptionHandler.CatchUnhandled(() => _threadContext.RunOnThread(() => _store.UpdateModel(model), true));

  /// <remarks>Operations must run on the main thread for ETABS and SAP 2000</remarks>
  public void RemoveModel(ModelCard model) =>
    _topLevelExceptionHandler.CatchUnhandled(() => _threadContext.RunOnThread(() => _store.RemoveModel(model), true));

  public Task HighlightModel(string modelCardId) => Task.CompletedTask;

  public Task HighlightObjects(IReadOnlyList<string> objectIds) => Task.CompletedTask;
}
