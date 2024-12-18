using Speckle.Converters.Common.Registration;

namespace Speckle.Converters.Common.Objects;

public interface IToSpeckleTopLevelConverter
{
  BaseResult Convert(object target);
}
