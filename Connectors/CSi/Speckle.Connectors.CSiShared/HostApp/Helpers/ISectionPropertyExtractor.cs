namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Core contract for section property extraction common across CSi products.
/// </summary>
public interface ISectionPropertyExtractor
{
  void ExtractProperties(string sectionName, Dictionary<string, object?> properties);
}

// NOTE: Seemingly silly, but allows us to register the correct extractor for the correct type.
public interface IFrameSectionPropertyExtractor : ISectionPropertyExtractor { }

public interface IShellSectionPropertyExtractor : ISectionPropertyExtractor { }
