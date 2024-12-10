using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class ClassPropertyExtractor
{
  private readonly CSiFrameToSpeckleConverter _frameConverter;
  private readonly CSiJointToSpeckleConverter _jointConverter;
  private readonly CSiShellToSpeckleConverter _shellConverter;

  public ClassPropertyExtractor(
    CSiFrameToSpeckleConverter frameConverter,
    CSiJointToSpeckleConverter jointConverter,
    CSiShellToSpeckleConverter shellConverter
  )
  {
    _frameConverter = frameConverter;
    _jointConverter = jointConverter;
    _shellConverter = shellConverter;
  }

  public Dictionary<string, object> GetProperties(ICSiWrapper wrapper)
  {
    return wrapper switch
    {
      CSiJointWrapper joint => _jointConverter.GetClassProperties(joint),
      CSiFrameWrapper frame => _frameConverter.GetClassProperties(frame),
      CSiShellWrapper shell => _shellConverter.GetClassProperties(shell),
      _ => new Dictionary<string, object>()
    };
  }
}
