using Rhino;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoContext
{
  public RhinoDoc Document { get; } = RhinoDoc.ActiveDoc;
}
