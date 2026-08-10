namespace Speckle.Connectors.GrasshopperShared.HostApp;

public static class Constants
{
  public const string LAYER_PATH_DELIMITER = "::";
  public const string PROPERTY_PATH_DELIMITER = ".";
  public const string TOPOLOGY_PROP = "topology";
  public const string NAME_PROP = "name";
  public const string PROPERTIES_PROP = "properties";

  /// <summary>Raised by the deprecated Publish components on every run.</summary>
  public const string DEPRECATED_PUBLISH_MESSAGE =
    "This Publish component is deprecated and creates a Speckle 3.0 version, so teammates on older connectors can "
    + "still read it. Switch to the new Publish component when your team is ready for 4.0.";

  /// <summary>Raised by the deprecated Load components when a version only exists as 4.0 artefacts.</summary>
  public const string DEPRECATED_LOAD_FALLBACK_MESSAGE =
    "This version was published with Speckle 4.0, so there is no 3.0 model to load. It has been loaded from 4.0 "
    + "artefacts instead: 'Properties' and 'Proxies' are unavailable, and objects may be split differently to what "
    + "this script expects. Switch to the new Load component for full 4.0 support.";
}
