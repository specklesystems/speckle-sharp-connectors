using Rhino;
using Rhino.DocObjects;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Helper class for common Rhino object operations.
/// </summary>
public static class RhinoObjectHelper
{
  /// <summary>
  /// Converts a string object ID to a RhinoObject.
  /// </summary>
  /// <returns>RhinoObject if found and valid, null otherwise</returns>
  public static RhinoObject? GetRhinoObject(string objectIdString) =>
    Guid.TryParse(objectIdString, out var objectId) ? RhinoDoc.ActiveDoc.Objects.FindId(objectId) : null;
}
