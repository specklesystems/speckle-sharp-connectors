namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Formalising required extracted results for ensuing operations
/// </summary>
public class SectionPropertyExtractionResult
{
  public string MaterialName { get; set; } // NOTE: Doubled up and nested in Properties, but we want quick access for relations
  public Dictionary<string, object?> Properties { get; set; } = [];
}

/// <summary>
/// Core contract for section property extraction common across CSi products.
/// </summary>
public interface ISectionPropertyExtractor
{
  void ExtractProperties(string sectionName, SectionPropertyExtractionResult dataExtractionResult);
}

// NOTE: Seemingly silly, but allows us to register the correct extractor for the correct type.
public interface IFrameSectionPropertyExtractor : ISectionPropertyExtractor { }

public interface IShellSectionPropertyExtractor : ISectionPropertyExtractor { }
