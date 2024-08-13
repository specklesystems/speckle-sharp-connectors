#if !REVIT2025
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;
using CefSharp;
using Revit.Async;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.Utils;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.Plugin;

internal sealed class RevitCefPlugin : IRevitPlugin
{
  private readonly UIControlledApplication _uIControlledApplication;
  private readonly IEnumerable<Lazy<IBinding>> _bindings; // should be lazy to ensure the bindings are not created too early
  private readonly BindingOptions _bindingOptions;
  private readonly RevitContext _revitContext;
  private readonly CefSharpPanel _cefSharpPanel;

  public RevitCefPlugin(
    UIControlledApplication uIControlledApplication,
    IEnumerable<Lazy<IBinding>> bindings,
    BindingOptions bindingOptions,
    RevitContext revitContext,
    CefSharpPanel cefSharpPanel
  )
  {
    _uIControlledApplication = uIControlledApplication;
    _bindings = bindings;
    _bindingOptions = bindingOptions;
    _revitContext = revitContext;
    _cefSharpPanel = cefSharpPanel;
  }

  public void Initialise()
  {
    // Create and register panels before app initialized. this is needed for double-click file open
    CreateTabAndRibbonPanel(_uIControlledApplication);
    RegisterDockablePane();
    _uIControlledApplication.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;
  }

  public void Shutdown()
  {
    // POC: should we be cleaning up the RibbonPanel etc...
    // Should we be indicating to any active in-flight functions that we are being closed?
  }

  // POC: Could be injected but maybe not worthwhile
  private void CreateTabAndRibbonPanel(UIControlledApplication application)
  {
    // POC: some top-level handling and feedback here
    try
    {
      application.CreateRibbonTab(Connector.TabName);
    }
    catch (ArgumentException)
    {
      // exception occurs when the speckle tab has already been created.
      // this happens when both the dui2 and the dui3 connectors are installed. Can be safely ignored.
    }

    RibbonPanel specklePanel = application.CreateRibbonPanel(Connector.TabTitle, Connector.TabTitle);
    var dui3Button = (PushButton)
      specklePanel.AddItem(
        new PushButtonData(
          Connector.Name,
          Connector.TabTitle,
          typeof(RevitExternalApplication).Assembly.Location,
          typeof(SpeckleRevitCommand).FullName
        )
      );

    string path = typeof(RevitCefPlugin).Assembly.Location;
    dui3Button.Image = LoadPngImgSource($"Speckle.Connectors.Revit{Connector.VersionString}.Assets.logo16.png", path);
    dui3Button.LargeImage = LoadPngImgSource(
      $"Speckle.Connectors.Revit{Connector.VersionString}.Assets.logo32.png",
      path
    );
    dui3Button.ToolTipImage = LoadPngImgSource(
      $"Speckle.Connectors.Revit{Connector.VersionString}.Assets.logo32.png",
      path
    );
    dui3Button.ToolTip = "Speckle Connector for Revit New UI";
    //dui3Button.AvailabilityClassName = typeof(CmdAvailabilityViews).FullName;
    dui3Button.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://speckle.systems"));
  }

  private void OnApplicationInitialized(object sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e)
  {
    var uiApplication = new UIApplication(sender as Application);
    _revitContext.UIApplication = uiApplication;

    // POC: might be worth to interface this out, we shall see...
    RevitTask.Initialize(uiApplication);

    PostApplicationInit(); // for double-click file open
  }

  /// <summary>
  /// Actions to run after UiApplication initialized. This is needed for double-click file open issue.
  /// </summary>
  private void PostApplicationInit()
  {
    // binding the bindings to each bridge
    foreach (IBinding binding in _bindings.Select(x => x.Value))
    {
      Debug.WriteLine(binding.Name);
      binding.Parent.AssociateWithBinding(binding);
    }

    _cefSharpPanel.Browser.IsBrowserInitializedChanged += (sender, e) =>
    {
      if (e.NewValue is false)
      {
        return;
      }

#if DEBUG
      _cefSharpPanel.Browser.ShowDevTools();
#endif
      foreach (IBinding binding in _bindings.Select(x => x.Value))
      {
        IBridge bridge = binding.Parent;

        _cefSharpPanel.Browser.JavascriptObjectRepository.Register(
          bridge.FrontendBoundName,
          bridge,
          true,
          _bindingOptions
        );
      }
    };
  }

  private void RegisterDockablePane()
  {
    CefSharpSettings.ConcurrentTaskExecution = true;

    // Registering dockable pane should happen before UiApplication is initialized with RevitTask.
    // Otherwise pane cannot be registered for double-click file open.
    _uIControlledApplication.RegisterDockablePane(
      RevitExternalApplication.DockablePanelId,
      Connector.Name,
      _cefSharpPanel
    );
  }

  private ImageSource? LoadPngImgSource(string sourceName, string path)
  {
    try
    {
      var assembly = Assembly.LoadFrom(Path.Combine(path));
      var icon = assembly.GetManifestResourceStream(sourceName);
      PngBitmapDecoder decoder = new(icon, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
      ImageSource source = decoder.Frames[0];
      return source;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // POC: logging
    }

    return null;
  }
}
#endif
