using System.Reflection;
using ArcGIS.Desktop.Framework;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.ArcGIS.HostApp;
using Speckle.Core.Common;
using Speckle.Core.Kits;
using Module = ArcGIS.Desktop.Framework.Contracts.Module;

namespace Speckle.Connectors.ArcGIS;

/// <summary>
/// This sample shows how to implement pane that contains an Edge WebView2 control using the built-in ArcGIS Pro SDK's WebBrowser control.  For details on how to utilize the WebBrowser control in an add-in see here: https://github.com/Esri/arcgis-pro-sdk/wiki/ProConcepts-Framework#webbrowser  For details on how to utilize the Microsoft Edge web browser control in an add-in see here: https://github.com/Esri/arcgis-pro-sdk/wiki/ProConcepts-Framework#webbrowser-control
/// </summary>
internal sealed class SpeckleModule : Module
{
  private static SpeckleModule? s_this;
  private readonly Speckle.Connectors.Utils.Connector _connector;

  /// <summary>
  /// Retrieve the singleton instance to this module here
  /// </summary>
  public static SpeckleModule Current =>
    s_this ??= (SpeckleModule)FrameworkApplication.FindModule("ConnectorArcGIS_Module");

  public SpeckleContainer Container => _connector.NotNull().Container;

  public SpeckleModule()
  {
    // Register Settings
    var arcgisSettings = new ArcGISSettings(HostApplications.ArcGIS, HostAppVersion.v3);
    var builder = SpeckleContainerBuilder
      .CreateInstance()
      .LoadAutofacModules(Assembly.GetExecutingAssembly(), arcgisSettings.Modules)
      .AddSingleton(arcgisSettings);

    _connector = Speckle.Connectors.Utils.Connector.Start<SpeckleModule>(
      builder,
      new(HostApplications.ArcGIS, "v3") //HostAppVersion.v3
    );
  }

  /// <summary>
  /// Called by Framework when ArcGIS Pro is closing
  /// </summary>
  /// <returns>False to prevent Pro from closing, otherwise True</returns>
  protected override bool CanUnload()
  {
    //TODO - add your business logic
    _connector.Dispose();
    //return false to ~cancel~ Application close
    return true;
  }
}
