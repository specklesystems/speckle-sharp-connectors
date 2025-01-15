namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

public class SectionPropertyExtractionResult
{
  public string MaterialName { get; set; }
  public Dictionary<string, object?> Properties { get; set; } = [];
}

/// <summary>
/// Core contract for section property extraction common across CSi products.
/// </summary>
public interface ISectionPropertyExtractor
{
  void ExtractProperties(string sectionName, SectionPropertyExtractionResult dataExtractionResult);
}

public interface IFrameSectionPropertyExtractor : ISectionPropertyExtractor { }

public interface IShellSectionPropertyExtractor : ISectionPropertyExtractor { }
