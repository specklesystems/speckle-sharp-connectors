using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class DisplayValueExtractor
{
  public DisplayValueExtractor() { }

  public IEnumerable<Base> GetDisplayValue(ICSiWrapper wrapper)
  {
    return wrapper switch
    {
      CSiJointWrapper joint => ExtractJoint(joint.Name),
      CSiFrameWrapper frame => ExtractFrame(frame.Name),
      CSiShellWrapper shell => ExtractShell(shell.Name),
      _ => Enumerable.Empty<Base>()
    };
  }

  private IEnumerable<Base> ExtractJoint(string name) => throw new NotImplementedException();

  private IEnumerable<Base> ExtractFrame(string name) => throw new NotImplementedException();

  private IEnumerable<Base> ExtractShell(string name) => throw new NotImplementedException();
}
