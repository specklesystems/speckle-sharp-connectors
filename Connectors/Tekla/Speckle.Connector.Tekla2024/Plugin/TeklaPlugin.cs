using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Sdk.Host;
using Tekla.Structures.Plugins;

namespace Speckle.Connector.Tekla2024.Plugin;

[Plugin("Speckle.Connectors.Tekla")]
[PluginUserInterface("Speckle.Connector.Tekla2024.SpeckleTeklaPanelHost")]
[InputObjectDependency(InputObjectDependency.NOT_DEPENDENT)]
public class TeklaPlugin : PluginBase
{
  public static ServiceProvider? Container { get; private set; }
  private IDisposable? _disposableLogger;
#pragma warning disable IDE1006
  public override bool Run(List<InputDefinition> Input)
  {
    var services = new ServiceCollection();
    _disposableLogger = services.Initialize(HostApplications.TeklaStructures, GetVersion());
    services.AddTekla();
    // TODO: Add Tekla converters

    Container = services.BuildServiceProvider();
    Container.UseDUI(); // TODO: this might not needed? ISyncToThread?

    return true;
  }
#pragma warning restore IDE1006

  public override List<InputDefinition> DefineInput() => new();

  private HostAppVersion GetVersion()
  {
#if TEKLA2024
    return HostAppVersion.v2024;
#else
    throw new NotImplementedException();
#endif
  }
}
