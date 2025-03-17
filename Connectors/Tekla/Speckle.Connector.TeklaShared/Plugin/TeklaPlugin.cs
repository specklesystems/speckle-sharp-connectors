using Tekla.Structures.Plugins;

namespace Speckle.Connectors.TeklaShared.Plugin;

[Plugin("Speckle")]
[PluginUserInterface("Speckle.Connectors.TeklaShared.SpeckleTeklaPanelHost")]
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
