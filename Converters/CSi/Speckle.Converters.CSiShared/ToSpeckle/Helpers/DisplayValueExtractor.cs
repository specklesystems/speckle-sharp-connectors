using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class DisplayValueExtractor
{
  private readonly ITypedConverter<CSiJointWrapper, Point> _jointConverter;
  private readonly ITypedConverter<CSiFrameWrapper, Line> _frameConverter;
  private readonly ITypedConverter<CSiShellWrapper, Mesh> _shellConverter;

  public DisplayValueExtractor(
    ITypedConverter<CSiJointWrapper, Point> jointConverter,
    ITypedConverter<CSiFrameWrapper, Line> frameConverter,
    ITypedConverter<CSiShellWrapper, Mesh> shellConverter
  )
  {
    _jointConverter = jointConverter;
    _frameConverter = frameConverter;
    _shellConverter = shellConverter;
  }

  public IEnumerable<Base> GetDisplayValue(ICSiWrapper wrapper)
  {
    return wrapper switch
    {
      CSiJointWrapper joint => ExtractJoint(joint),
      CSiFrameWrapper frame => ExtractFrame(frame),
      CSiShellWrapper shell => ExtractShell(shell),
      _ => Enumerable.Empty<Base>()
    };
  }

  private IEnumerable<Base> ExtractJoint(CSiJointWrapper target)
  {
    yield return _jointConverter.Convert(target);
  }

  private IEnumerable<Base> ExtractFrame(CSiFrameWrapper target)
  {
    yield return _frameConverter.Convert(target);
  }

  private IEnumerable<Base> ExtractShell(CSiShellWrapper target) => throw new NotImplementedException();
}
