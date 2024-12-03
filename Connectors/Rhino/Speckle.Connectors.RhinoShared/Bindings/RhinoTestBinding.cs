using System.Diagnostics;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Rhino.Bindings;

public interface IHostAppTestBinding : IBinding
{
  string[] GetTests();
}

public class RhinoTestBinding : IHostAppTestBinding
{
  public string Name => "hostAppTestBiding";
  public IBrowserBridge Parent { get; }

  public RhinoTestBinding(IBrowserBridge parent)
  {
    Parent = parent;
  }

  public void ExecuteTest(string testName)
  {
    Debug.WriteLine(testName);
  }

  public string[] GetTests()
  {
    return ["pasta", "test"];
  }

  public void TestCaseOne() { }

  public void TestCaseTwo() { }
}
