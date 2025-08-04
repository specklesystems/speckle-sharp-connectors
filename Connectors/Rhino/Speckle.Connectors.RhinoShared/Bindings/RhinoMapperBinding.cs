using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Rhino.Bindings;

/// <summary>
/// Binding for managing Rhino object mappings to Revit categories.
/// </summary>
public class RhinoMapperBinding : IBinding
{
  public string Name { get; }
  public IBrowserBridge Parent { get; }
}
