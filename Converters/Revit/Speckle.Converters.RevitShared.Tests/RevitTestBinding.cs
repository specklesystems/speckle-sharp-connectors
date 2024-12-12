using System.Reflection;
using Speckle.Connectors.DUI.Bridge;
using Speckle.HostApps;

namespace Speckle.Converters.Revit2023.Tests;


public sealed class RevitTestBinding(ITestExecutorFactory testExecutorFactory, IBrowserBridge parent) : TestBindingBase(testExecutorFactory)
{
  public override IEnumerable<Assembly> GetAssemblies()
  {
    yield return Assembly.GetExecutingAssembly();
  }

  public override IBrowserBridge Parent { get; } = parent;
}
