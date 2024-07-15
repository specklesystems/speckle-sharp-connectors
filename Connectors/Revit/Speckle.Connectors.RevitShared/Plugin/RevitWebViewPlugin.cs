#if REVIT2025
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Core.Logging;

namespace Speckle.Connectors.Revit.Plugin;

internal sealed class RevitWebViewPlugin : IRevitPlugin
{
  private readonly UIControlledApplication _uIControlledApplication;
  private readonly RevitSettings _revitSettings;
  private readonly RevitContext _revitContext;
  private readonly DUI3ControlWebViewDockable _webViewPanel;

  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Style",
    "IDE0290:Use primary constructor",
    Justification = "<Pending>"
  )]
  public RevitWebViewPlugin(
    UIControlledApplication uIControlledApplication,
    RevitSettings revitSettings,
    RevitContext revitContext,
    DUI3ControlWebViewDockable webViewPanel
  )
  {
    _uIControlledApplication = uIControlledApplication;
    _revitSettings = revitSettings;
    _revitContext = revitContext;
    _webViewPanel = webViewPanel;
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
      application.CreateRibbonTab(_revitSettings.RevitTabName);
    }
    catch (ArgumentException)
    {
      // exception occurs when the speckle tab has already been created.
      // this happens when both the dui2 and the dui3 connectors are installed. Can be safely ignored.
    }

    RibbonPanel specklePanel = application.CreateRibbonPanel(_revitSettings.RevitTabName, _revitSettings.RevitTabTitle);
    var dui3Button = (PushButton)
      specklePanel.AddItem(
        new PushButtonData(
          _revitSettings.RevitButtonName,
          _revitSettings.RevitButtonText,
          typeof(RevitExternalApplication).Assembly.Location,
          typeof(SpeckleRevitCommand).FullName
        )
      );

    string path = typeof(RevitWebViewPlugin).Assembly.Location;
    dui3Button.Image = LoadPngImgSource(
      $"Speckle.Connectors.Revit{_revitSettings.RevitVersionName}.Assets.logo16.png",
      path
    );
    dui3Button.LargeImage = LoadPngImgSource(
      $"Speckle.Connectors.Revit{_revitSettings.RevitVersionName}.Assets.logo32.png",
      path
    );
    dui3Button.ToolTipImage = LoadPngImgSource(
      $"Speckle.Connectors.Revit{_revitSettings.RevitVersionName}.Assets.logo32.png",
      path
    );
    dui3Button.ToolTip = "Speckle Connector for Revit New UI";
    //dui3Button.AvailabilityClassName = typeof(CmdAvailabilityViews).FullName;
    dui3Button.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://speckle.systems"));
  }

  private void OnApplicationInitialized(object? sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e)
  {
    var uiApplication = new UIApplication(sender as Application);
    _revitContext.UIApplication = uiApplication;

    // POC: might be worth to interface this out, we shall see...
    //RevitTask.Initialize(uiApplication);
  }

  private void RegisterDockablePane()
  {
    // Registering dockable pane should happen before UiApplication is initialized with RevitTask.
    // Otherwise pane cannot be registered for double-click file open.
    _uIControlledApplication.RegisterDockablePane(
      RevitExternalApplication.DoackablePanelId,
      _revitSettings.RevitPanelName,
      _webViewPanel
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
