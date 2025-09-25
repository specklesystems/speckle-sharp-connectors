namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiToSpeckleCacheSingleton
{
  /// <summary>
  /// A map of (material id, section ids). Assumes the material id is the unique name of the material
  /// </summary>
  public Dictionary<string, List<string>> MaterialCache { get; set; } = [];

  /// <summary>
  /// A map of (section id, frame object id). Assumes the section id is the unique name of the section
  /// </summary>
  public Dictionary<string, List<string>> FrameSectionCache { get; set; } = [];

  /// <summary>
  /// A map of (section id, shell object id). Assumes the section id is the unique name of the section
  /// </summary>
  public Dictionary<string, List<string>> ShellSectionCache { get; set; } = [];
}
