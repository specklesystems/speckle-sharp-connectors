using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Handles Revit Views per Send/Receive, e.g. determines whether the View is supported for specific operation.
/// </summary>
public class RevitViewManager
{
  /// <summary>
  /// Determine if the View is supported for Receive operation. Currently only 3d view or horizontal 2d views are supported.
  /// Views like Section, Elevation, ViewSheet etc. are not supported
  /// </summary>
  public bool IsSupportedReceiveView(View activeView)
  {
    switch (activeView.ViewType)
    {
      case ViewType.ThreeD:
      case ViewType.FloorPlan:
      case ViewType.AreaPlan:
      case ViewType.CeilingPlan:
        return true;
      case ViewType.Detail:
        return IsHorizontalView(activeView);

      default:
        return false;
    }
  }

  public bool Is3dView(View activeView)
  {
    if (activeView.ViewType == ViewType.ThreeD)
    {
      return true;
    }
    return false;
  }

  private bool IsHorizontalView(View activeView)
  {
    if (Math.Abs(activeView.ViewDirection.Z - 1) < 0.00001)
    {
      return true;
    }
    return false;
  }
}
