using Tekla.Structures.Plugins;

namespace Speckle.Connector.Tekla2024.Plugin;

[Plugin("Speckle.Connectors.Tekla")]
[PluginUserInterface("Speckle.Connector.Tekla2024.SpeckleTeklaPanelHost")]
[InputObjectDependency(InputObjectDependency.NOT_DEPENDENT)] // See DevDocs/InputObjectDependency.NOT_DEPENDENT.png
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
