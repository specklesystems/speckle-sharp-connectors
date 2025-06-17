using Grasshopper.Kernel.Data;

namespace Speckle.Connectors.GrasshopperShared.Components;

public static class IGH_StructureExtensions
{
  public static bool HasInputCountGreaterThan(this IGH_Structure data, int maximumCount, bool skipNulls = false)
  {
    return data.AllData(skipNulls).Skip(maximumCount).Any();
  }
}
