using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.TSDShared.HostApp;

namespace Speckle.Connectors.TSDShared.Bindings;

internal sealed class TSDSelectionBinding : ISelectionBinding
{
  private readonly ITSDApplicationService _applicationService;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private SelectionInfo _selection = new([], "No objects selected.");

  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }

  public TSDSelectionBinding(
    IBrowserBridge parent,
    ITSDApplicationService applicationService,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
  {
    Parent = parent;
    _applicationService = applicationService;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _applicationService.SelectionChanged += OnSelectionChanged;
  }

  public SelectionInfo GetSelection() => _selection;

  private void OnSelectionChanged(object? sender, EventArgs e) =>
    _topLevelExceptionHandler.FireAndForget(async () => await UpdateSelectionAsync());

  private async Task UpdateSelectionAsync()
  {
    _selection = await GetSelectionInfoAsync().ConfigureAwait(false);
    await Parent.Send(SelectionBindingEvents.SET_SELECTION, _selection).ConfigureAwait(false);
  }

  private async Task<SelectionInfo> GetSelectionInfoAsync()
  {
    var entities = await _applicationService.GetSelectedEntitiesAsync().ConfigureAwait(false);
    if (entities.Count == 0)
    {
      return new SelectionInfo([], "No objects selected.");
    }

    var ids = entities.Select(entity => entity.EncodedId).ToList();
    var typeNames = string.Join(", ", entities.Select(entity => entity.TypeName).Distinct());
    return new SelectionInfo(ids, $"{ids.Count} objects ({typeNames})");
  }
}
