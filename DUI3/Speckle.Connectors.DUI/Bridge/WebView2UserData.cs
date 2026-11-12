namespace Speckle.Connectors.DUI.Bridge;

public static class WebView2UserData
{
  public static string FolderPath { get; } =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Speckle", "WebView2");
}
