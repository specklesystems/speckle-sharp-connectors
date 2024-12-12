using System.Reflection;
using Speckle.Connectors.DUI.Bridge;
using Speckle.HostApps;

namespace Speckle.Connectors.Rhino.Bindings;

public sealed class RhinoTestBinding(ITestExecutorFactory testExecutorFactory, IBrowserBridge parent)
  : TestBindingBase(testExecutorFactory)
{
  public override IEnumerable<Assembly> GetAssemblies()
  {
    yield return Assembly.GetExecutingAssembly();
  }

  public override IBrowserBridge Parent { get; } = parent;
}
