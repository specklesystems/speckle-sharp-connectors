using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.AutocadShared.ToHost.Raw;

public class IntervalToHostRawConverter : ITypedConverter<SOP.Interval, AG.Interval>
{
  /// <exception cref="ArgumentNullException"> Throws if target start or end value is null.</exception>
  public AG.Interval Convert(SOP.Interval target)
  {
    // POC: the tolerance might be in some settings or in some context?
    return new(target.start, target.end, 0.000);
  }
}
