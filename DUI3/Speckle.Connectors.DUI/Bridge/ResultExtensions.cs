using System.Runtime.InteropServices;

namespace Speckle.Connectors.DUI.Bridge;

public static class ResultExtensions
{
  public static void ShowExceptionDialog(this Result result, string action)
  {
    if (result.IsSuccess)
    {
      return;
    }
    ShowExceptionTaskDialog(result.Exception, action);
  }
  
  private static void ShowExceptionTaskDialog(Exception ex, string action)
  {
    var config = new TaskDialogConfig
    {
      cbSize = (uint)Marshal.SizeOf(typeof(TaskDialogConfig)),
      pszWindowTitle = "Unhandled Exception",
      pszMainInstruction = action,
      pszContent = ex.Message,
      pszExpandedInformation = ex.ToString(),
      pszExpandedControlText = "Show Details",
      pszCollapsedControlText = "Hide Details",
      dwCommonButtons = TaskDialogCommonButtons.Ok,
      dwFlags = TaskDialogFlags.ExpandFooterArea | TaskDialogFlags.ExpandedByDefault
    };

    var _ = TaskDialogIndirect(ref config, out int _, out int _, out bool _);
  }
  
  [DllImport("comctl32.dll", SetLastError = true)]
#pragma warning disable CA5392
  private static extern int TaskDialogIndirect(
#pragma warning restore CA5392
    [In] ref TaskDialogConfig pTaskConfig,
    out int pnButton,
    out int pnRadioButton,
    [MarshalAs(UnmanagedType.Bool)] out bool pfVerificationFlagChecked);

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
#pragma warning disable IDE1006
  private struct TaskDialogConfig
  {
    public uint cbSize;
    public IntPtr hwndParent;
    public IntPtr hInstance;
    public TaskDialogFlags dwFlags;
    public TaskDialogCommonButtons dwCommonButtons;
    [MarshalAs(UnmanagedType.LPWStr)] public string pszWindowTitle;
    public IntPtr hMainIcon;
    [MarshalAs(UnmanagedType.LPWStr)] public string pszMainInstruction;
    [MarshalAs(UnmanagedType.LPWStr)] public string pszContent;
    public uint cButtons;
    public IntPtr pButtons;
    public int nDefaultButton;
    public uint cRadioButtons;
    public IntPtr pRadioButtons;
    public int nDefaultRadioButton;
    [MarshalAs(UnmanagedType.LPWStr)] public string pszVerificationText;
    [MarshalAs(UnmanagedType.LPWStr)] public string pszExpandedInformation;
    [MarshalAs(UnmanagedType.LPWStr)] public string pszExpandedControlText;
    [MarshalAs(UnmanagedType.LPWStr)] public string pszCollapsedControlText;
    public IntPtr pszFooter;
    public IntPtr hFooterIcon;
    public IntPtr pfCallback;
    public IntPtr lpCallbackData;
    public uint cxWidth;
  }
#pragma warning restore IDE1006

  [Flags]
  private enum TaskDialogFlags : uint
  {
    EnableHyperlinks = 0x0001,
    UseHIconMain = 0x0002,
    UseHIconFooter = 0x0004,
    AllowDialogCancellation = 0x0008,
    UseCommandLinks = 0x0010,
    UseCommandLinksNoIcon = 0x0020,
    ExpandFooterArea = 0x0040,
    ExpandedByDefault = 0x0080,
    VerificationFlagChecked = 0x0100,
    ShowProgressBar = 0x0200,
    ShowMarqueeProgressBar = 0x0400,
    CallbackTimer = 0x0800,
    PositionRelativeToWindow = 0x1000,
    RtlLayout = 0x2000,
    NoDefaultRadioButton = 0x4000,
    CanBeMinimized = 0x8000
  }
  [Flags]
  private enum TaskDialogCommonButtons : uint
  {
    None   = 0x0000,
    Ok     = 0x0001,
    Yes    = 0x0002,
    No     = 0x0004,
    Cancel = 0x0008,
    Retry  = 0x0010,
    Close  = 0x0020
  }
}
