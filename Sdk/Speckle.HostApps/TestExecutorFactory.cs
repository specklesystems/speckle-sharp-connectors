using System.Reflection;
using Speckle.InterfaceGenerator;

namespace Speckle.HostApps;

[GenerateAutoInterface]
public class TestExecutorFactory : ITestExecutorFactory
{
  public TestExecutor Create(Assembly assembly) => new (assembly);
}
