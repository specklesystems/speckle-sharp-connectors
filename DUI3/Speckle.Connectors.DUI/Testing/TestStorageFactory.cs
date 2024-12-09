using Speckle.InterfaceGenerator;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.DUI.Testing;

[GenerateAutoInterface]
public class TestStorageFactory : ITestStorageFactory
{
  private ITestStorage Create(string path) => new TestStorage(path);

  public ITestStorage CreateForUser() =>
    Create(Path.Combine(SpecklePathProvider.UserApplicationDataPath(), "Speckle", "Testing.db"));
}
