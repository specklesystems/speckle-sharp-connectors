namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Contract for host application specific section property extraction.
/// </summary>
/// <remarks>
/// Mirrors property extraction system pattern by composing with base extractor.
/// Enables both shared and application-specific property extraction in one call.
/// </remarks>
public interface IApplicationSectionPropertyExtractor
{
  void ExtractProperties(string sectionName, SectionPropertyExtractionResult dataExtractionResult);
}

public interface IApplicationFrameSectionPropertyExtractor : IApplicationSectionPropertyExtractor { }

public interface IApplicationShellSectionPropertyExtractor : IApplicationSectionPropertyExtractor { }
