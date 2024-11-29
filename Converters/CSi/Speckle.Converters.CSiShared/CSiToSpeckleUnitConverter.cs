using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared;

// TODO: Unit conversions! This is all gross, I know!
public class CSiToSpeckleUnitConverter : IHostToSpeckleUnitConverter<string>
{
#pragma warning disable IDE0060
  public string ToSpeckle(string value)
#pragma warning restore IDE0060
  {
    // CSi default is meters
    return "m";
  }

#pragma warning disable IDE0060
  public string FromSpeckle(string value)
#pragma warning restore IDE0060
  {
    return "m";
  }

  public string ConvertOrThrow(string hostUnit)
  {
    return hostUnit ?? throw new ArgumentNullException(nameof(hostUnit), "Host unit cannot be null");
  }
}
