// using System.Runtime.InteropServices;

using System.Diagnostics.CodeAnalysis;
using System.Windows.Automation;

namespace Speckle.Converter.Navisworks.Helpers;

/// <summary>
/// Represents the visual mode settings for Navisworks conversion.
/// </summary>
public readonly struct VisualModes(bool surfaces, bool lines, bool points)
{
  public bool Surfaces { get; } = surfaces;

  public bool Lines { get; } = lines;

  public bool Points { get; } = points;
}

/// <summary>
/// Reads the current Navisworks "Surfaces", "Lines" and "Points" toggle states once.
/// </summary>
[SuppressMessage("Style", "IDE0059:Unnecessary assignment of a value")]
public static class VisualModeCheck
{
  // private const int WM_SETREDRAW = 0x000B;
  //
  // [DllImport("user32.dll", CharSet = CharSet.Auto)]
  // [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  // private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
  //
  // [DllImport("user32.dll", SetLastError = true)]
  // [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  // private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

  private const string SURFACES_ID = "RoamerGUI_OM_PRIMITIVE_TRIANGLES";
  private const string LINES_ID = "RoamerGUI_OM_PRIMITIVE_LINES";
  private const string POINTS_ID = "RoamerGUI_OM_PRIMITIVE_POINTS";
  private const string SNAPPOINTS_ID = "RoamerGUI_OM_PRIMITIVE_SNAP_POINTS";
  private const string TEXT_ID = "RoamerGUI_OM_PRIMITIVE_TEXT";

  /// <summary>
  /// One‐shot snapshot using AutomationIds in the Raw view.
  /// </summary>
  public static VisualModes Current
  {
    get
    {
      // var mainHwnd = NavisworksApp.Gui.MainWindow.Handle;

      // SendMessage(mainHwnd, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

      var root =
        AutomationElement.FromHandle(NavisworksApp.Gui.MainWindow.Handle)
        ?? throw new InvalidOperationException("Navisworks window not found.");

      var tabs = root.FindAll(
        TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)
      );

      var originalTab = tabs.Cast<AutomationElement>()
        .FirstOrDefault(t =>
          t.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sip)
          && ((SelectionItemPattern)sip).Current.IsSelected
        )
        ?.Current.Name;

      var buttonsByNames = root.FindAll(
        TreeScope.Descendants,
        new AndCondition(
          new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "Triangles"),
            new PropertyCondition(AutomationElement.NameProperty, "Surfaces"),
            new PropertyCondition(AutomationElement.NameProperty, "Lines"),
            new PropertyCondition(AutomationElement.NameProperty, "Points")
          ),
          new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
        )
      );

      var buttonsByIds = root.FindAll(
        TreeScope.Descendants,
        new AndCondition(
          new OrCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, SURFACES_ID),
            new PropertyCondition(AutomationElement.AutomationIdProperty, LINES_ID),
            new PropertyCondition(AutomationElement.AutomationIdProperty, POINTS_ID),
            new PropertyCondition(AutomationElement.AutomationIdProperty, SNAPPOINTS_ID),
            new PropertyCondition(AutomationElement.AutomationIdProperty, TEXT_ID)
          ),
          new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
        )
      );

      var buttonNameList = new List<(string? name, string? id, bool? isEnabled)>();

      foreach (AutomationElement button in buttonsByNames)
      {
        var name = button.GetCurrentPropertyValue(AutomationElement.NameProperty) as string;
        var id = button.GetCurrentPropertyValue(AutomationElement.AutomationIdProperty) as string;
        var isEnabled = button.Current.IsEnabled;

        var patternId = (AutomationPattern)
          typeof(AutomationElement)
            .GetField(
              "LegacyIAccessiblePatternId",
              System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            )!
            .GetValue(null);
        button.TryGetCurrentPattern(patternId, out var iaObj);

        buttonNameList.Add((name, id, isEnabled)!);
      }

      var buttonIdList = new List<(string? name, string? id, bool? isEnabled)>();

      foreach (AutomationElement button in buttonsByIds)
      {
        var name = button.GetCurrentPropertyValue(AutomationElement.NameProperty) as string;
        var id = button.GetCurrentPropertyValue(AutomationElement.AutomationIdProperty) as string;
        var isEnabled = button.Current.IsEnabled;
        buttonIdList.Add((name, id, isEnabled)!);
      }

      var modes = new VisualModes(ReadById(root, SURFACES_ID), ReadById(root, LINES_ID), ReadById(root, POINTS_ID));

      // SendMessage(mainHwnd, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
      // InvalidateRect(mainHwnd, IntPtr.Zero, true);

      return modes;
    }
  }

  private static bool ReadById(AutomationElement root, string automationId)
  {
    // search the Raw tree so offscreen/collapsed controls show up
    var elt = FindByAutomationIdRaw(root, automationId);
    if (elt == null)
    {
      return false;
    }

    // try TogglePattern
    if (elt.TryGetCurrentPattern(TogglePattern.Pattern, out var tpObj))
    {
      return ((TogglePattern)tpObj).Current.ToggleState == ToggleState.On;
    }

    // final fallback: Enabled
    bool isEnabled = elt.Current.IsEnabled;

    return isEnabled;
  }

  private static AutomationElement? FindByAutomationIdRaw(AutomationElement root, string automationId) =>
    FindByAutomationIdRawRecursive(root, TreeWalker.RawViewWalker, automationId);

  private static AutomationElement? FindByAutomationIdRawRecursive(
    AutomationElement node,
    TreeWalker walker,
    string automationId
  )
  {
    // 1) test this node
    if (node.GetCurrentPropertyValue(AutomationElement.AutomationIdProperty) as string == automationId)
    {
      return node;
    }

    // 2) recurse into its Raw children
    for (var child = walker.GetFirstChild(node); child != null; child = walker.GetNextSibling(child))
    {
      var found = FindByAutomationIdRawRecursive(child, walker, automationId);
      if (found != null)
      {
        return found;
      }
    }

    return null;
  }
}
