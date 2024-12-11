using Speckle.Connectors.DUI.Testing;

namespace Speckle.Connectors.DUI.Bindings;

public interface IHostAppTestBinding : IBinding
{
  string GetLoadedModel();
  ModelTest[] GetTests();
  ModelTestResult[] GetTestsResults();
}
