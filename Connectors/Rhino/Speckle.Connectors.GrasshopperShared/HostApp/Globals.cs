namespace Speckle.Connectors.GrasshopperShared.HostApp;

public static class Constants
{
  public const string LAYER_PATH_DELIMITER = "::";
  public const string PROPERTY_PATH_DELIMITER = ".";
  public const string TOPOLOGY_PROP = "topology";
  public const string NAME_PROP = "name";
  public const string PROPERTIES_PROP = "properties";

  /// <summary>The user-facing name of the release the artefact path ships in. Internally still "4.0" / artefacts.</summary>
  private const string NEW_VERSION = "2026.9.0";

  /// <summary>Remark raised by the artefact-first Load components when a version has no bundle to read.</summary>
  public const string LEGACY_FALLBACK_MESSAGE =
    $"This version has no Speckle {NEW_VERSION} bundle, so it was loaded from the 3.0 model. Object grouping and "
    + $"collection paths may differ from a {NEW_VERSION} load.";

  /// <summary>Remark raised by the deprecated Load components on every solve.</summary>
  public const string DEPRECATED_LOAD_MESSAGE =
    $"This Load component is deprecated. Switch to the new Load component for Speckle {NEW_VERSION} support.";

  /// <summary>Remark raised by the deprecated Publish components on every solve.</summary>
  public const string DEPRECATED_PUBLISH_MESSAGE =
    $"This Publish component is deprecated. Switch to the new Publish component for Speckle {NEW_VERSION} support.";

  /// <summary>Warning raised by the deprecated Publish components when they actually publish.</summary>
  public const string PUBLISHED_LEGACY_VERSION_MESSAGE =
    "This publish creates a Speckle 3.0 version so teammates on older connectors can still read it. The new Publish "
    + $"component creates {NEW_VERSION} versions.";

  /// <summary>Raised by the deprecated Load components when a version only exists as artefacts.</summary>
  public const string DEPRECATED_LOAD_FALLBACK_MESSAGE =
    $"This version was published with Speckle {NEW_VERSION}, so there is no 3.0 model to load. It has been loaded "
    + $"from {NEW_VERSION} artefacts instead: 'Properties' and 'Proxies' are unavailable, and objects may be split "
    + "differently to what this script expects. Switch to the new Load component for full "
    + $"{NEW_VERSION} support.";

  /// <summary>Warning raised by either Load when the artefact path throws after the legacy path already failed.</summary>
  public const string ARTEFACT_LOAD_FAILED_MESSAGE = $"{NEW_VERSION} artefact load also failed";

  /// <summary>Remark raised by Explore when no bundle is cached for the piped-in object's version.</summary>
  public const string EXPLORE_NO_GRAPH_MESSAGE =
    $"No {NEW_VERSION} graph is cached for this model - it was loaded from a 3.0 version, or the cache has been "
    + "cleared. Reload the model to explore it.";

  /// <summary>Remark raised by Explore when the piped-in object carries no object index.</summary>
  public const string EXPLORE_NO_REFERENCE_MESSAGE =
    "This object has no graph reference. It came from inside a block definition, or from a 3.0 load.";
}
