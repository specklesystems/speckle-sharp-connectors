using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using CefSharp;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Revit;

public partial class CefSharpPanel : Page, Autodesk.Revit.UI.IDockablePaneProvider, IBrowserScriptExecutor
{
  public CefSharpPanel()
  {
    InitializeComponent();
  }

  /// <inheritdoc/>
  public void ExecuteScript(string script, CancellationToken cancellationToken)
  {
    if (!Browser.CheckAccess())
    {
      ExecuteScriptDispatched(script, cancellationToken);
      return;
    }

    //avoid exceptions by checking if IBrowser is there
    if (!Browser.IsBrowserInitialized || Browser.GetBrowser() is null)
    {
      return;
    }

    Browser.ExecuteScriptAsync(script);
  }

  /// <inheritdoc/>
  public void ExecuteScriptDispatched(string script, CancellationToken cancellationToken)
  {
    if (Browser == null || !Browser.IsInitialized)
    {
      throw new InvalidOperationException("Failed to execute script, ChromiumWebBrowser is not initialized yet");
    }

    //Intentionally using the dispatcher even from the main thread
    //As it allows the UI to pump messages, and stay responsive
    Browser.Dispatcher.Invoke(
      () =>
      {
        //avoid exceptions by checking if IBrowser is there
        if (!Browser.IsBrowserInitialized || Browser.GetBrowser() is null)
        {
          return;
        }

        Browser.ExecuteScriptAsync(script);
      },
      DispatcherPriority.Background,
      cancellationToken
    );
  }

  public bool IsBrowserInitialized => Browser.IsBrowserInitialized;
  public object BrowserElement => Browser;

  public void ShowDevTools() => Browser.ShowDevTools();

  public void SetupDockablePane(Autodesk.Revit.UI.DockablePaneProviderData data)
  {
    data.FrameworkElement = this;
    data.InitialState = new Autodesk.Revit.UI.DockablePaneState
    {
      DockPosition = DockPosition.Tabbed,
      TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
    };
  }
}
