namespace Speckle.Connector.Navisworks.Services;

/// <summary>
/// Connector-specific element selection service that extends the converter's base implementation.
/// Inherits the cached visibility checking and path resolution from the converter layer.
/// </summary>
public class ElementSelectionService : Converter.Navisworks.Services.ElementSelectionService
{
  // This inherits all functionality from the converter's ElementSelectionService
  // including cached IsVisible, GetModelItemPath, GetModelItemFromPath, and GetGeometryNodes
  // Connector-specific extensions can be added here if needed in the future
}
