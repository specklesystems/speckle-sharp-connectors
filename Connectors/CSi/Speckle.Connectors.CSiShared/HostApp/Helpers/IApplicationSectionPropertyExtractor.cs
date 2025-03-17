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
  void ExtractProperties(string sectionName, Dictionary<string, object?> properties);
}

// NOTE: Seemingly silly, but allows us to register the correct extractor for the correct type.
public interface IApplicationFrameSectionPropertyExtractor : IApplicationSectionPropertyExtractor { }

public interface IApplicationShellSectionPropertyExtractor : IApplicationSectionPropertyExtractor { }
