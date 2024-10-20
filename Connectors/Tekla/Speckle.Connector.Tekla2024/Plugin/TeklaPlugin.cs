using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Tekla2024.Bindings;
using Speckle.Connector.Tekla2024.HostApp;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Tekla.Structures.Plugins;

namespace Speckle.Connector.Tekla2024.Plugin;

[Plugin("Speckle.Connectors.Tekla")]
[PluginUserInterface("Speckle.Connector.Tekla2024.SpeckleTeklaPanelHost")]
[InputObjectDependency(InputObjectDependency.NOT_DEPENDENT)] // See DevDocs/InputObjectDependency.NOT_DEPENDENT.png
public class TeklaPlugin : PluginBase
{
#pragma warning disable IDE1006
  private static IServiceProvider _serviceProvider;
  private static IBrowserBridge _browserBridge;
  private static ISelectionBinding _selectionBinding;

  static TeklaPlugin()
  {
    InitializeSpeckle();
  }

  private static void InitializeSpeckle()
  {
    var services = new ServiceCollection();

    services.AddSingleton<ISelectionBinding, TeklaSelectionBinding>();
    services.AddSingleton<IBrowserBridge, BrowserBridge>();
    services.AddSingleton<IAppIdleManager, TeklaIdleManager>();

    _serviceProvider = services.BuildServiceProvider();

    _browserBridge = _serviceProvider.GetRequiredService<IBrowserBridge>();
    _selectionBinding = _serviceProvider.GetRequiredService<ISelectionBinding>();

    _browserBridge.AssociateWithBinding(_selectionBinding);
  }

  public override bool Run(List<InputDefinition> Input)
  {
    return true;
  }
#pragma warning restore IDE1006

  public override List<InputDefinition> DefineInput() => new();
}
