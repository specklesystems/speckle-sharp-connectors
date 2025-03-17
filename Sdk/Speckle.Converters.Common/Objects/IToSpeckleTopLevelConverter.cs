using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.Objects;

public interface IToSpeckleTopLevelConverter
{
  Base Convert(object target);
}
