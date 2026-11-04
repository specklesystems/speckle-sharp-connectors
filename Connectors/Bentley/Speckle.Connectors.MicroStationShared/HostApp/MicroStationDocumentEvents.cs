using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.MicroStation.Plugin;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Subscribes to MicroStation 2026 COM events (model activate / model change)
/// and notifies the DUI3 panel so it can refresh its document state.
/// </summary>
public sealed class MicroStationDocumentEvents
{
  private readonly IServiceProvider _serviceProvider;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IAppIdleManager _idleManager;
  private readonly ModelActivateHandler _activateHandler;

  public MicroStationDocumentEvents(
    IServiceProvider serviceProvider,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IAppIdleManager idleManager
  )
  {
    _serviceProvider = serviceProvider;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _idleManager = idleManager;

    _activateHandler = new ModelActivateHandler(ScheduleDocumentRefresh);
    var app = MsApp.TryGetInstance();
    app?.AddModelActivateEventsHandler(_activateHandler);
  }

  private void ScheduleDocumentRefresh() =>
    _topLevelExceptionHandler.CatchUnhandled(() =>
      _idleManager.SubscribeToIdle(nameof(ProcessDocumentChangeAsync), async () => await ProcessDocumentChangeAsync())
    );

  private async Task ProcessDocumentChangeAsync()
  {
    var store = _serviceProvider.GetRequiredService<MicroStationDocumentModelStore>();
    store.ReloadState();

    var basicBinding = _serviceProvider.GetRequiredService<IBasicConnectorBinding>();
    if (basicBinding is Bindings.MicroStationBasicConnectorBinding binding)
    {
      await binding.Commands.NotifyDocumentChanged();
    }
  }
}

/// <summary>
/// COM event sink for IModelActivateEvents.
/// MicroStation calls <see cref="AfterActivate"/> when the active model changes
/// (file open, model switch, design file close-and-reopen).
/// </summary>
[ComVisible(true)]
internal sealed class ModelActivateHandler : MSIDGN.IModelActivateEvents
{
  private readonly Action _onActivate;

  public ModelActivateHandler(Action onActivate) => _onActivate = onActivate;

  public void AfterActivate(ModelReference theModel) => _onActivate();

  public void BeforeActivate(ModelReference theModel) { }
}
