using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.Utils;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.CSiShared.Utils;
using Timer = System.Timers.Timer;

namespace Speckle.Connectors.CSiShared.Bindings;

public class CsiSharedSelectionBinding : ISelectionBinding, IDisposable
{
  private bool _disposed;
  private readonly Timer _selectionTimer;
  private readonly ICsiApplicationService _csiApplicationService;
  private HashSet<string> _lastSelection = new();

  public IBrowserBridge Parent { get; }
  public string Name => "selectionBinding";

  public CsiSharedSelectionBinding(
    IBrowserBridge parent,
    ICsiApplicationService csiApplicationService,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
  {
    Parent = parent;
    _csiApplicationService = csiApplicationService;

    _selectionTimer = new Timer(1000);
    _selectionTimer.Elapsed += (_, _) => topLevelExceptionHandler.CatchUnhandled(CheckSelectionChanged);
    _selectionTimer.Start();
  }

  private void CheckSelectionChanged()
  {
    var currentSelection = GetSelection();
    var currentIds = new HashSet<string>(currentSelection.SelectedObjectIds);

    if (!_lastSelection.SetEquals(currentIds))
    {
      _lastSelection = currentIds;
      Parent.Send(SelectionBindingEvents.SET_SELECTION, currentSelection);
    }
  }

  protected virtual void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        _selectionTimer?.Dispose();
      }
      _disposed = true;
    }
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Gets the selection and creates an encoded ID (objectType and objectName).
  /// </summary>
  /// <remarks>
  /// Refer to ObjectIdentifier.cs for more info.
  /// </remarks>
  public SelectionInfo GetSelection()
  {
    int numberItems = 0;
    int[] objectType = [];
    string[] objectName = [];

    _csiApplicationService.SapModel.SelectObj.GetSelected(ref numberItems, ref objectType, ref objectName);

    var encodedIds = new List<string>(numberItems);
    var typeCounts = new Dictionary<string, int>();

    for (int i = 0; i < numberItems; i++)
    {
      var typeKey = (ModelObjectType)objectType[i];
      var typeName = typeKey.ToString();

      encodedIds.Add(ObjectIdentifier.Encode(objectType[i], objectName[i]));
      typeCounts[typeName] = (typeCounts.TryGetValue(typeName, out var count) ? count : 0) + 1; // NOTE: Cross-framework compatibility (net 48 and net8)
    }

    var summary =
      encodedIds.Count == 0
        ? "No objects selected."
        : $"{encodedIds.Count} objects ({string.Join(", ", 
            typeCounts.Select(kv => $"{kv.Value} {kv.Key}"))})";

    return new SelectionInfo(encodedIds, summary);
  }
}
