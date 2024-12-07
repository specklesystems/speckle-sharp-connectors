namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Interfaces for property extractors
/// </summary>
public interface IPropertyExtractor
{
  Dictionary<string, object?>? ExtractProperties(ICsiWrapper wrapper);
}

public interface IGeneralPropertyExtractor : IPropertyExtractor { }

public interface IClassPropertyExtractor : IPropertyExtractor { }
