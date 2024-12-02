using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared;

// NOTE: These are just temporarily here. Should be in SDK
[SpeckleType("Converters.CSiShared.CSiObject")]
public class CSiObject : Base, ICSiObject
{
  public required string name { get; set; }
  public required string type { get; set; }

  [DetachProperty]
#pragma warning disable IDE1006
  public required List<Base> displayValue { get; set; }
#pragma warning restore IDE1006

#pragma warning disable IDE1006
  public required string units { get; set; }
#pragma warning restore IDE1006

  IReadOnlyList<Base> IDataObject.displayValue => displayValue;
}

public interface ICSiObject : IDataObject
{
#pragma warning disable IDE1006
  string type { get; }
#pragma warning restore IDE1006
}
