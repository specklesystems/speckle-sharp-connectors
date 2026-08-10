namespace Speckle.Connectors.GrasshopperShared.HostApp;

public static class Constants
{
  public const string LAYER_PATH_DELIMITER = "::";
  public const string PROPERTY_PATH_DELIMITER = ".";
  public const string TOPOLOGY_PROP = "topology";
  public const string NAME_PROP = "name";
  public const string PROPERTIES_PROP = "properties";

  /// <summary>Remark raised by the deprecated Load components on every solve.</summary>
  public const string DEPRECATED_LOAD_MESSAGE =
    "This Load component is deprecated. Switch to the new Load component for Speckle 4.0 support.";

  /// <summary>Remark raised by the deprecated Publish components on every solve.</summary>
  public const string DEPRECATED_PUBLISH_MESSAGE =
    "This Publish component is deprecated. Switch to the new Publish component for Speckle 4.0 support.";

  /// <summary>Warning raised by the deprecated Publish components when they actually publish.</summary>
  public const string PUBLISHED_LEGACY_VERSION_MESSAGE =
    "This publish creates a Speckle 3.0 version so teammates on older connectors can still read it. The new Publish "
    + "component creates 4.0 versions.";

  /// <summary>Raised by the deprecated Load components when a version only exists as 4.0 artefacts.</summary>
  public const string DEPRECATED_LOAD_FALLBACK_MESSAGE =
    "This version was published with Speckle 4.0, so there is no 3.0 model to load. It has been loaded from 4.0 "
    + "artefacts instead: 'Properties' and 'Proxies' are unavailable, and objects may be split differently to what "
    + "this script expects. Switch to the new Load component for full 4.0 support.";
}
