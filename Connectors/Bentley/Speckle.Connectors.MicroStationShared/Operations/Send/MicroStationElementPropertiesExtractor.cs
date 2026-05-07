namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// Extracts a properties dictionary from a MicroStation DGN <see cref="Element"/> COM object.
/// Captures the standard built-in attributes (level, color, line style/weight, transparency,
/// element type, modification timestamp, locked/new flags, hyperlink) that any DGN-platform
/// product (MicroStation, OpenRoads Designer, OpenBridge Modeler) exposes via the COM API.
/// <para>
/// EC instance properties (Bentley engineering content schema) and XAttribute user data are
/// NOT extracted here — the COM <see cref="Element"/> surface doesn't expose them, and
/// reaching into <c>Bentley.DgnPlatformNET</c> for them has historically caused process-
/// terminating CSEs during Send (see <c>FallbackElementMeshConverter</c> remarks). Extend
/// this extractor in a future pass once a robust native-interop strategy is in place.
/// </para>
/// </summary>
internal static class MicroStationElementPropertiesExtractor
{
  public static Dictionary<string, object?> Extract(Element element)
  {
    var properties = new Dictionary<string, object?>(capacity: 16)
    {
      ["elementType"] = element.Type.ToString(),
      ["elementId"] = element.ID.ToString(),
      ["color"] = SafeRead(() => element.Color),
      ["lineWeight"] = SafeRead(() => element.LineWeight),
      ["displayPriority"] = SafeRead(() => element.DisplayPriority),
      ["transparency"] = SafeRead(() => element.Transparency),
      ["isLocked"] = SafeRead(() => element.IsLocked),
      ["isModified"] = SafeRead(() => element.IsModified),
      ["isNew"] = SafeRead(() => element.IsNew),
    };

    var levelName = SafeRead(() => element.Level?.Name);
    if (!string.IsNullOrEmpty(levelName))
    {
      properties["level"] = levelName;
    }

    var styleName = SafeRead(() => element.LineStyle?.Name);
    if (!string.IsNullOrEmpty(styleName))
    {
      properties["lineStyle"] = styleName;
    }

    var url = SafeRead(() => element.URL);
    if (!string.IsNullOrEmpty(url))
    {
      properties["url"] = url;
      properties["urlTitle"] = SafeRead(() => element.URLTitle);
    }

    return properties;
  }

  // COM property reads can throw HRESULT exceptions on edge cases (orphaned elements,
  // pre-conversion state). Swallow them so a single bad property doesn't sink the whole element.
  private static T? SafeRead<T>(Func<T> read)
  {
    try
    {
      return read();
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      return default;
    }
  }
}
