namespace Speckle.Converters.Civil3dShared.Helpers;

/// <summary>
/// Constructs a the Corridor Key for a subassembly with the Corridor Handle, Baseline Guid, Region Guid, Assembly Handle, and Subassembly Handle.
/// This order and type is determined by the structure of corridors and the available information on extracted corridor solid property sets.
/// </summary>
public readonly struct SubassemblyCorridorKey
{
  public string CorridorId { get; }
  public string BaselineId { get; }
  public string RegionId { get; }
  public string AssemblyId { get; }
  public string SubassemblyId { get; }

  public SubassemblyCorridorKey(
    string corridorHandle,
    string baselineGuid,
    string regionGuid,
    string assemblyHandle,
    string subassemblyHandle
  )
  {
    CorridorId = corridorHandle.ToLower();
    BaselineId = CleanGuidString(baselineGuid.ToLower());
    RegionId = CleanGuidString(regionGuid.ToLower());
    AssemblyId = assemblyHandle.ToLower();
    SubassemblyId = subassemblyHandle.ToLower();
  }

  // Removes brackets from guid strings - property sets will return guids with brackets (unlike when retrieved from api)
  private string CleanGuidString(string guid)
  {
    guid = guid.Replace("{", "").Replace("}", "");
    return guid;
  }

  public override readonly string ToString() => $"{CorridorId}-{BaselineId}-{RegionId}-{AssemblyId}-{SubassemblyId}";
}
