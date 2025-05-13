using ArcGIS.Desktop.Framework;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.ArcGIS.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Converters.ArcGIS3;
using Module = ArcGIS.Desktop.Framework.Contracts.Module;

namespace Speckle.Connectors.ArcGIS;

/// <summary>
/// This sample shows how to implement pane that contains an Edge WebView2 control using the built-in ArcGIS Pro SDK's WebBrowser control.  For details on how to utilize the WebBrowser control in an add-in see here: https://github.com/Esri/arcgis-pro-sdk/wiki/ProConcepts-Framework#webbrowser  For details on how to utilize the Microsoft Edge web browser control in an add-in see here: https://github.com/Esri/arcgis-pro-sdk/wiki/ProConcepts-Framework#webbrowser-control
/// </summary>
internal sealed class SpeckleModule : Module
{
  private static SpeckleModule? s_this;
  private readonly IDisposable? _disposableLogger;

  /// <summary>
  /// Retrieve the singleton instance to this module here
  /// </summary>
  public static SpeckleModule Current =>
    s_this ??= (SpeckleModule)FrameworkApplication.FindModule("ConnectorArcGIS_Module");

  public ServiceProvider Container { get; }

  public SpeckleModule()
  {
    AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.OnAssemblyResolve<SpeckleModule>;

    var services = new ServiceCollection();
    // init DI
    _disposableLogger = services.Initialize(HostApplications.ArcGIS, GetVersion());
    services.AddArcGIS();
    services.AddArcGISConverters();
    Container = services.BuildServiceProvider();
    Container.UseDUI();
  }

  private HostAppVersion GetVersion()
  {
#if ARCGIS3
    return HostAppVersion.v3;
#else
    throw new NotImplementedException();
#endif
  }

  /// <summary>
  /// Called by Framework when ArcGIS Pro is closing
  /// </summary>
  /// <returns>False to prevent Pro from closing, otherwise True</returns>
  protected override bool CanUnload()
  {
    //TODO - add your business logic
    //return false to ~cancel~ Application close
    _disposableLogger?.Dispose();
    Container.Dispose();
    return true;
  }
}
