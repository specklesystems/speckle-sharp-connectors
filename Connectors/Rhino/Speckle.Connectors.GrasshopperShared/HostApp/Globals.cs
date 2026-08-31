namespace Speckle.Connectors.GrasshopperShared.HostApp;

public static class Constants
{
  public const string LAYER_PATH_DELIMITER = "::";
  public const string PROPERTY_PATH_DELIMITER = ".";
  public const string TOPOLOGY_PROP = "topology";
  public const string NAME_PROP = "name";
  public const string PROPERTIES_PROP = "properties";

  /// <summary>
  /// The support floor, and only that. Use it for what a component supports ("{RELEASE} onwards") or for what a
  /// version predates - both stay true as later releases ship. Never use it to say which release produced a given
  /// version: that goes stale the moment the next one lands, and those messages talk about components instead.
  /// Everything before the floor is "older", never a version of its own - two numbers invite a comparison we don't
  /// want. Internally this is still the "4.0" / artefact path.
  /// </summary>
  private const string RELEASE = "Speckle 2026.9.0";

  /// <summary>Remark raised by the artefact-first Load components when a version has no bundle to read.</summary>
  public const string LEGACY_FALLBACK_MESSAGE =
    $"This version was published before {RELEASE}. Object grouping and collection paths may differ from versions "
    + "published since.";

  /// <summary>Remark raised by the deprecated Load components on every solve.</summary>
  public const string DEPRECATED_LOAD_MESSAGE =
    $"This Load component is deprecated. The current Load component supports {RELEASE} onwards.";

  /// <summary>Remark raised by the deprecated Publish components on every solve.</summary>
  public const string DEPRECATED_PUBLISH_MESSAGE =
    $"This Publish component is deprecated. The current Publish component supports {RELEASE} onwards.";

  /// <summary>Warning raised by the deprecated Publish components when they actually publish.</summary>
  public const string PUBLISHED_LEGACY_VERSION_MESSAGE =
    "This component publishes versions that older connectors can still read. Versions from the current Publish "
    + "component need an upgraded connector - check your collaborators before switching.";

  /// <summary>Raised by the deprecated Load components when a version only exists as artefacts.</summary>
  public const string DEPRECATED_LOAD_FALLBACK_MESSAGE =
    "This version needs the current Load component. It has been loaded here as far as possible, but 'Properties' "
    + "and 'Proxies' are empty and objects may be split differently to what this script expects.";

  /// <summary>Warning raised by either Load when the artefact path throws after the legacy path already failed.</summary>
  public const string ARTEFACT_LOAD_FAILED_MESSAGE = "Could not load this version";

  /// <summary>Remark raised by Explore when no bundle is cached for the piped-in object's version.</summary>
  public const string EXPLORE_NO_GRAPH_MESSAGE =
    $"There is nothing to explore for this model. Explore supports versions published with {RELEASE} onwards, loaded "
    + "with the current Load component.";

  /// <summary>Remark raised by Explore when the piped-in object carries no object index.</summary>
  public const string EXPLORE_NO_REFERENCE_MESSAGE =
    "This object can't be explored. It came from inside a block definition, or was not loaded with the current Load "
    + "component.";
}
