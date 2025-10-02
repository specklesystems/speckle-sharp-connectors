namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Contract for host application specific material property extraction.
/// </summary>
/// <remarks>
/// Mirrors property extraction system pattern by composing with base extractor.
/// Enables both shared and application-specific property extraction in one call.
/// </remarks>
public interface IApplicationMaterialPropertyExtractor
{
  void ExtractProperties(string name, Dictionary<string, object?> properties);
}
