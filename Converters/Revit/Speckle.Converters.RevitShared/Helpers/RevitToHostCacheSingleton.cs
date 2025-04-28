namespace Speckle.Converters.RevitShared.Helpers;

public class RevitToHostCacheSingleton
{
  /// <summary>
  /// POC: Not sure is there a way to create it on "RevitHostObjectBuilder" with a scope instead singleton. For now we fill this dictionary and clear it on "RevitHostObjectBuilder".
  /// Map extracted by revit material baker to be able to use it in converter.
  /// This is needed because we cannot set materials for meshes in connector.
  /// They needed to be set while creating "TessellatedFace".
  /// </summary>
  public Dictionary<string, DB.ElementId> MaterialsByObjectId { get; } = new();

  /// <summary>
  /// Stores the reference point transform extracted from the model
  /// </summary>
  public DB.Transform? ReferencePointTransform { get; set; }
}
