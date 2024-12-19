using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.Objects;

public interface IToHostTopLevelConverter
{
  HostResult Convert(Base target);
}
