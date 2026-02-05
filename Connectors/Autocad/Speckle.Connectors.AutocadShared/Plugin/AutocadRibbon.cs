using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using Speckle.Sdk;
#if !AUTOCAD2025_OR_GREATER && !CIVIL3D2025_OR_GREATER
using System.IO;
#endif

namespace Speckle.Connectors.Autocad.Plugin;

public class AutocadRibbon
{
  private RibbonControl? _ribbon;
  private readonly AutocadCommand _command;

  public AutocadRibbon(AutocadCommand command)
  {
    _command = command;
  }

  public bool CreateRibbon()
  {
    _ribbon = ComponentManager.Ribbon;
    try
    {
      if (_ribbon != null) //the assembly was loaded using netload
      {
        Create();
      }
      else
      {
        // load the custom ribbon on startup, but wait for ribbon control to be created
        ComponentManager.ItemInitialized += new System.EventHandler<RibbonItemEventArgs>(
          ComponentManager_ItemInitialized
        );
        Application.SystemVariableChanged += TrapWSCurrentChange;
      }
    }
    catch (System.Exception ex) when (!ex.IsFatal())
    {
      return false;
      // todo: report error
    }

    return true;
  }

  private void Create()
  {
    RibbonTab tab = FindOrMakeTab("Speckle");
    RibbonPanelSource source = new() { Title = "Speckle" };
    RibbonPanel panel = new() { Source = source };
    tab.Panels.Add(panel);

    RibbonToolTip speckleToolTip =
      new()
      {
        Title = "Speckle",
        Content = $"Next Gen Speckle Connector for {AppUtils.App.Name}",
        IsHelpEnabled = true, // Without this "Press F1 for help" does not appear in the tooltip
      };

    _ = CreateSpeckleButton("Speckle", source, null, speckleToolTip, "logo");
  }

  private void ComponentManager_ItemInitialized(object? sender, RibbonItemEventArgs e)
  {
    // one Ribbon item is initialized, check for Ribbon control
    _ribbon = ComponentManager.Ribbon;
    if (_ribbon != null)
    {
      Create();
      // remove the event handler
      ComponentManager.ItemInitialized -= new System.EventHandler<RibbonItemEventArgs>(
        ComponentManager_ItemInitialized
      );
    }
  }

  // solving workspace changing
  private void TrapWSCurrentChange(object sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
  {
    if (e.Name.Equals("WSCURRENT"))
    {
      Create();
    }
  }

  private RibbonTab FindOrMakeTab(string name)
  {
    if (_ribbon is null)
    {
      throw new InvalidOperationException($"Ribbon control was null, could not create tab {name}");
    }

    RibbonTab? tab = _ribbon.Tabs.FirstOrDefault(o => o.Title.Equals(name)); // check to see if tab exists
    if (tab is null) // if not, create a new one
    {
      tab = new RibbonTab { Title = name, Id = name };
      _ribbon.Tabs.Add(tab);
    }

    return tab;
  }

  private RibbonButton CreateSpeckleButton(
    string name,
    RibbonPanelSource? sourcePanel = null,
    RibbonSplitButton? sourceButton = null,
    RibbonToolTip? tooltip = null,
    string imageName = ""
  )
  {
    var button = new RibbonButton
    {
      // ribbon panel source info assignment
      Text = name,
      Id = name,
      ShowImage = true,
      ShowText = true,
      ToolTip = tooltip,
      HelpSource = new System.Uri("https://speckle.guide/user/autocadcivil.html"),
      Size = RibbonItemSize.Large,
    };

    if (TryLoadPngImgSource(imageName + "16.png", out ImageSource? imageSource))
    {
      button.Image = imageSource;
    }

    if (TryLoadPngImgSource(imageName + "32.png", out ImageSource? largeImageSource))
    {
      button.LargeImage = largeImageSource;
    }

    // add ribbon button panel to the ribbon panel source
    if (sourcePanel != null)
    {
      button.Orientation = System.Windows.Controls.Orientation.Vertical;
      button.CommandParameter = AutocadCommand.COMMAND_STRING;
      button.CommandHandler = new SpeckleButtonCommandHandler(_command);
      sourcePanel.Items.Add(button);
    }
    else if (sourceButton != null)
    {
      button.Orientation = System.Windows.Controls.Orientation.Horizontal;
      button.CommandParameter = AutocadCommand.COMMAND_STRING;
      button.CommandHandler = new SpeckleButtonCommandHandler(_command);
      sourceButton.Items.Add(button);
    }

    return button;
  }

  /// <summary>
  /// Retrieve the png image source
  /// </summary>
  /// <param name="sourceName"></param>
  /// <returns></returns>
  private bool TryLoadPngImgSource(string sourceName, out System.Windows.Media.ImageSource? source)
  {
    source = null;
    if (string.IsNullOrEmpty(sourceName) || !sourceName.ToLower().EndsWith(".png"))
    {
      return false;
    }

    Assembly assembly = Assembly.GetExecutingAssembly();
    string[] assemblyResources = GetType().Assembly.GetManifestResourceNames();
    string? resource = assemblyResources.Where(o => o.EndsWith(sourceName)).FirstOrDefault();
    if (string.IsNullOrEmpty(resource))
    {
      return false;
    }

    Stream? stream = null;
    try
    {
      stream = assembly.GetManifestResourceStream(resource);
    }
    catch (FileLoadException) { }
    catch (FileNotFoundException) { }
    catch (NotImplementedException) { }

    if (stream is null)
    {
      return false;
    }

    PngBitmapDecoder decoder = new(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
    if (decoder.Frames.Count == 0)
    {
      return false;
    }

    source = decoder.Frames[0];
    return true;
  }
}

public class SpeckleButtonCommandHandler : System.Windows.Input.ICommand
{
  private readonly AutocadCommand _autocadCommand;

  public SpeckleButtonCommandHandler(AutocadCommand autocadCommand)
  {
    _autocadCommand = autocadCommand;
  }

#pragma warning disable 67
  // Disabling warning for `event never used` since this is required by autocad
  public event System.EventHandler? CanExecuteChanged;
#pragma warning restore 67

  public void Execute(object? parameter)
  {
    if (parameter is RibbonButton)
    {
      _autocadCommand.Command();
    }
  }

  public bool CanExecute(object? parameter) => true;
}
