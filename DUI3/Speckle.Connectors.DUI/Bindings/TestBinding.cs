using System.Diagnostics;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Sdk;

namespace Speckle.Connectors.DUI.Bindings;

/// <summary>
/// POC: This is a class that sanity checks basic bridge functionality. It is required by the frontend's tests page.
/// </summary>
public class TestBinding : IBinding
{
  public string Name => "testBinding";
  public IBrowserBridge Parent { get; }

  public TestBinding(IBrowserBridge bridge)
  {
    Parent = bridge;
  }

  public string SayHi(string name, int count, bool sayHelloNotHi)
  {
    var baseGreeting = $"{(sayHelloNotHi ? "Hello" : "Hi")} {name}!";
    var finalGreeting = "";
    for (int i = 0; i < Math.Max(1, Math.Abs(count)); i++)
    {
      finalGreeting += baseGreeting + Environment.NewLine;
    }

    return finalGreeting;
  }

  public void ShouldThrow() => throw new SpeckleException("I am supposed to throw.");

  public void GoAway() => Debug.WriteLine("Okay, going away.");

  public object GetComplexType() =>
    new
    {
      Id = GetHashCode() + " - I am a string",
      count = GetHashCode(),
      thisIsABoolean = false
    };

  public async Task TriggerEvent(string eventName)
  {
    switch (eventName)
    {
      case "emptyTestEvent":
        await Parent.Send("emptyTestEvent");

        break;
      case "testEvent":
      default:
        await Parent.Send(
          "testEvent",
          new
          {
            IsOk = true,
            Name = "foo",
            Count = 42
          }
        );
        break;
    }
  }
}
