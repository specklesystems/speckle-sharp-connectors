using Rhino;
using Speckle.Connectors.GrasshopperShared.HostApp.Extras;
using Speckle.Sdk;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

/// <summary>
/// Monitors Rhino doc events (creating, opening, switching) and notifies subscribed components when documents change.
/// These aren't caught by Grasshopper's DocumentContextChanged.
/// </summary>
/// <remarks>
/// Not tied to any specific component - doc events are global. When any doc changes, all interested components should know.
/// </remarks>
public static class DocChangeMonitor
{
  // list of components that want to be notified. WeakReference better in preventing memory leaks (?)
  private static readonly List<WeakReference<IDocChangeListener>> s_listeners = new();

  // coming from v2. preventing rapid-fire events from causing chaos
  private static readonly DebounceDispatcher s_debouncer = new();

  static DocChangeMonitor()
  {
    // Subscribe to Rhino's doc changes (I don't know if all three are needed?)
    RhinoDoc.NewDocument += OnDocumentChanged;
    RhinoDoc.BeginOpenDocument += OnDocumentChanged;
    RhinoDoc.EndOpenDocument += OnDocumentChanged;
  }

  /// <summary>
  /// Subscribe to document change notifications.
  /// Uses WeakReference to prevent memory leaks.
  /// </summary>
  public static void Subscribe(IDocChangeListener listener) =>
    s_listeners.Add(new WeakReference<IDocChangeListener>(listener));

  /// <summary>
  /// Unsubscribe from document change notifications.
  /// Call this in Dispose() or RemovedFromDocument() to clean up.
  /// </summary>
  public static void Unsubscribe(IDocChangeListener listener)
  {
    for (int i = s_listeners.Count - 1; i >= 0; i--)
    {
      if (s_listeners[i].TryGetTarget(out var target) && ReferenceEquals(target, listener))
      {
        s_listeners.RemoveAt(i);
        break;
      }
    }
  }

  private static void OnDocumentChanged(object sender, EventArgs e) =>
    // Debounce rapid document events (open events can fire multiple times)
    s_debouncer.Debounce(50, _ => NotifyListeners());

  private static void NotifyListeners()
  {
    // Clean up dead references while notifying live ones
    for (int i = s_listeners.Count - 1; i >= 0; i--)
    {
      if (s_listeners[i].TryGetTarget(out var listener))
      {
        try
        {
          // Ensure UI thread for component operations
          RhinoApp.InvokeOnUiThread(() => listener.OnDocumentChanged());
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          // Don't let one bad listener break others
          System.Diagnostics.Debug.WriteLine($"Error notifying document change listener: {ex.Message}");
        }
      }
      else
      {
        // Remove dead reference
        s_listeners.RemoveAt(i);
      }
    }
  }
}

/// <summary>
/// Interface for components that need to respond to Rhino document changes.
/// Implement this to get notified when documents are created, opened, or switched.
/// </summary>
public interface IDocChangeListener
{
  /// <summary>
  /// Called when a Rhino document change occurs.
  /// This includes new documents, opened files, and document switching.
  /// </summary>
  void OnDocumentChanged();
}
