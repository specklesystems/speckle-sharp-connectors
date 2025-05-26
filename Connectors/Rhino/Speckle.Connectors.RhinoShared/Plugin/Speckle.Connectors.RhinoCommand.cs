using System.IO;
using System.Reflection;
using Rhino;
using Rhino.Commands;
using Rhino.Input.Custom;
using Rhino.UI;
using Speckle.Connectors.Rhino.HostApp;
#if RHINO8_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.DUI.WebView;
#endif

namespace Speckle.Connectors.Rhino.Plugin;

public class SpeckleConnectorsRhinoCommand : Command
{
  public SpeckleConnectorsRhinoCommand()
  {
    // Rhino only creates one instance of each command class defined in a
    // plug-in, so it is safe to store a reference in a static property.
    Instance = this;
    string iconPath = Path.Combine(GetAssemblyDirectory(), "Resources", "speckle32.ico");
    Panels.RegisterPanel(
      SpeckleConnectorsRhinoPlugin.Instance,
      typeof(SpeckleRhinoPanelHost),
      "Speckle",
      new Icon(iconPath),
      PanelType.System
    );
  }

  // Method to get the directory of the currently executing assembly
  private string GetAssemblyDirectory()
  {
    var assembly = Assembly.GetExecutingAssembly();
    if (assembly == null)
    {
      throw new InvalidOperationException("Unable to get executing assembly.");
    }

    string codeBase = assembly.Location; // Use Location instead of CodeBase
    if (string.IsNullOrEmpty(codeBase))
    {
      throw new InvalidOperationException("Assembly location is null or empty.");
    }

    string? path = Path.GetDirectoryName(codeBase);
    if (path == null)
    {
      throw new InvalidOperationException("Unable to determine directory from assembly location.");
    }

    return path;
  }

  ///<summary>The only instance of this command.</summary>
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
  public static SpeckleConnectorsRhinoCommand Instance { get; private set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

  ///<returns>The command name as it appears on the Rhino command line.</returns>
  public override string EnglishName => "Speckle";

  protected override Result RunCommand(RhinoDoc doc, RunMode mode)
  {
    Guid panelId = typeof(SpeckleRhinoPanelHost).GUID;

    if (mode == RunMode.Interactive)
    {
#if RHINO8_OR_GREATER
      SpeckleRhinoPanelHost.Reinitialize(
        SpeckleConnectorsRhinoPlugin.Instance.Container?.GetRequiredService<DUI3ControlWebView>()
      );
#endif
      Panels.OpenPanel(panelId);
      return Result.Success;
    }

    bool panelVisible = Panels.IsPanelVisible(panelId);

    string prompt = panelVisible
      ? "SpeckleWebUIWebView2 panel is visible. New value"
      : "SpeckleWebUIWebView2 panel is hidden. New value";

    using GetOption go = new();
    go.SetCommandPrompt(prompt);
    int hideIndex = go.AddOption("Hide");
    int showIndex = go.AddOption("Show");
    int toggleIndex = go.AddOption("Toggle");
    go.Get();

    if (go.CommandResult() != Result.Success)
    {
      return go.CommandResult();
    }

    CommandLineOption option = go.Option();
    if (null == option)
    {
      return Result.Failure;
    }

    int index = option.Index;
    if (index == hideIndex)
    {
      if (panelVisible)
      {
        Panels.ClosePanel(panelId);
      }
    }
    else if (index == showIndex)
    {
      if (!panelVisible)
      {
        Panels.OpenPanel(panelId);
      }
    }
    else if (index == toggleIndex)
    {
      switch (panelVisible)
      {
        case true:
          Panels.ClosePanel(panelId);
          break;
        default:
          Panels.OpenPanel(panelId);
          break;
      }
    }
    return Result.Success;
  }
}
