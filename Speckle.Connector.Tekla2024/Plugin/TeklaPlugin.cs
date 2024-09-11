using System.IO;
using System.Reflection;
using Speckle.Autofac.DependencyInjection;
using Speckle.Sdk.Common;
using Tekla.Structures.Plugins;

namespace Speckle.Connector.Tekla2024.Plugin;

[Plugin("Speckle.Connectors.Tekla")]
[PluginUserInterface("Speckle.Connector.Tekla2024.SpeckleTeklaPanelHost")]
public class TeklaPlugin : PluginBase
{
  public static SpeckleContainer Container { get; private set; }
#pragma warning disable IDE1006
  public override bool Run(List<InputDefinition> Input)
  {
    var builder = SpeckleContainerBuilder.CreateInstance();
    Container = builder
      .LoadAutofacModules(
        Assembly.GetExecutingAssembly(),
        [Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).NotNull()]
      )
      .Build();
    return true;
  }
#pragma warning restore IDE1006

  public override List<InputDefinition> DefineInput() => new();
}
