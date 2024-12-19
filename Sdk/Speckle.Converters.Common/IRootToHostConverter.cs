using Speckle.Sdk.Models;

namespace Speckle.Converters.Common;

public interface IRootToHostConverter
{
  HostResult Convert(Base target);
}
