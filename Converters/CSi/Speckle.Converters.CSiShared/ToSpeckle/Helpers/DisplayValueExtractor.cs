using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class DisplayValueExtractor
{
  private readonly ITypedConverter<CsiJointWrapper, Point> _jointConverter;
  private readonly ITypedConverter<CsiFrameWrapper, Line> _frameConverter;
  private readonly ITypedConverter<CsiShellWrapper, Mesh> _shellConverter;

  public DisplayValueExtractor(
    ITypedConverter<CsiJointWrapper, Point> jointConverter,
    ITypedConverter<CsiFrameWrapper, Line> frameConverter,
    ITypedConverter<CsiShellWrapper, Mesh> shellConverter
  )
  {
    _jointConverter = jointConverter;
    _frameConverter = frameConverter;
    _shellConverter = shellConverter;
  }

  public IEnumerable<Base> GetDisplayValue(ICsiWrapper wrapper)
  {
    return wrapper switch
    {
      CsiJointWrapper joint => ExtractJoint(joint),
      CsiFrameWrapper frame => ExtractFrame(frame),
      CsiShellWrapper shell => ExtractShell(shell),
      _ => Enumerable.Empty<Base>()
    };
  }

  private IEnumerable<Base> ExtractJoint(CsiJointWrapper target)
  {
    yield return _jointConverter.Convert(target);
  }

  private IEnumerable<Base> ExtractFrame(CsiFrameWrapper target)
  {
    yield return _frameConverter.Convert(target);
  }

  private IEnumerable<Base> ExtractShell(CsiShellWrapper target)
  {
    yield return _shellConverter.Convert(target);
  }
}
