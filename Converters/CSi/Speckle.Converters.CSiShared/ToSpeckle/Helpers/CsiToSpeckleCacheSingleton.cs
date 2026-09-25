using Speckle.Objects.Geometry;

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

  /// <summary>
  /// A cache of cross-sectional areas used
  /// </summary>
  public Dictionary<string, double> FrameSectionAreaCache { get; set; } = [];

  /// <summary>
  /// A cache of resolved shell section properties populated by "EtabsShellPropertiesExtractor"
  /// and consumed by "EtabsShellSectionPropertyExtractor".
  /// This eliminates redundant section resolution API calls.
  /// </summary>
  public Dictionary<string, Dictionary<string, object?>> ShellSectionPropertiesCache { get; set; } = [];

  /// <summary>
  /// Per-section unit prisms for volumetric display values, or the reason none could be built (ENG-9048).
  /// </summary>
  public Dictionary<string, FrameSectionPrism> FramePrismCache { get; set; } = [];

  /// <summary>
  /// Analytical node-to-node line per frame name, recorded when volumetric geometry is requested so the artefact
  /// builder can ship it as CENTERLINE while the solid takes DISPLAY (ENG-9048).
  /// </summary>
  public Dictionary<string, Line> FrameCenterlineCache { get; set; } = [];
}
