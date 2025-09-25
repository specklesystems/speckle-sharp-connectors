namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Core contract for material property extraction common across CSi products.
/// </summary>
public interface IMaterialPropertyExtractor
{
  void ExtractProperties(string name, Dictionary<string, object?> properties);
}
