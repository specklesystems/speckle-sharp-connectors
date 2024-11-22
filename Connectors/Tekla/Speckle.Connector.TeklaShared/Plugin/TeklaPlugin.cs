using Tekla.Structures.Plugins;

namespace Speckle.Connector.Tekla2024.Plugin;

[Plugin("Speckle")]
[PluginUserInterface("Speckle.Connector.Tekla2024.SpeckleTeklaPanelHost")]
[InputObjectDependency(InputObjectDependency.NOT_DEPENDENT)]
public class TeklaPlugin : PluginBase
{
#pragma warning disable IDE1006

  static TeklaPlugin() { }

  public override bool Run(List<InputDefinition> Input)
  {
    return true;
  }
#pragma warning restore IDE1006

  public override List<InputDefinition> DefineInput() => new();
}
