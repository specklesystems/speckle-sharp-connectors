using Grasshopper;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

/// <summary>
/// Simple utility to track Tab key state for name inheritance during parameter connections
/// </summary>
/// <remarks>
/// Taken from legacy (v2) code
/// </remarks>
public static class KeyWatcher
{
  public static bool TabPressed { get; private set; }

  private static bool s_initialized;

  /// <summary>
  /// Initialize the key watcher by hooking into Grasshopper canvas events
  /// Call this once during component initialization
  /// </summary>
  public static void Initialize()
  {
    if (s_initialized)
    {
      return;
    }

    // hook into canvas creation event
    Instances.CanvasCreated += OnCanvasCreated;

    // if canvas already exists, hook into it immediately
    if (Instances.ActiveCanvas != null)
    {
      HookIntoCanvas(Instances.ActiveCanvas);
    }

    s_initialized = true;
  }

  private static void OnCanvasCreated(Grasshopper.GUI.Canvas.GH_Canvas canvas)
  {
    if (canvas != null)
    {
      HookIntoCanvas(canvas);
    }
  }

  private static void HookIntoCanvas(Grasshopper.GUI.Canvas.GH_Canvas canvas)
  {
    canvas.KeyDown += (s, e) =>
    {
      if (e.KeyCode == Keys.Tab && !TabPressed)
      {
        TabPressed = true;
      }
    };

    canvas.KeyUp += (s, e) =>
    {
      if (TabPressed && e.KeyCode == Keys.Tab)
      {
        TabPressed = false;
      }
    };
  }
}
