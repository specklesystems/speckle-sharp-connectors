using Microsoft.Extensions.Logging;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.ETABSShared.HostApp.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.ETABSShared.HostApp;

/// <summary>
/// Unpacks and creates proxies for frame and shell sections from the model.
/// </summary>
/// <remarks>
/// Provides a unified approach to section extraction across different section types.
/// Leverages specialized extractors to handle complex property retrieval. Centralizes
/// section proxy creation with robust error handling and logging mechanisms.
/// </remarks>
public class EtabsSectionUnpacker : ISectionUnpacker
{
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly EtabsSectionPropertyExtractor _propertyExtractor;
  private readonly ILogger<EtabsSectionUnpacker> _logger;
  private readonly ISdkActivityFactory _activityFactory;

  public EtabsSectionUnpacker(
    ICsiApplicationService csiApplicationService,
    EtabsSectionPropertyExtractor propertyExtractor,
    ILogger<EtabsSectionUnpacker> logger,
    ISdkActivityFactory activityFactory
  )
  {
    _csiApplicationService = csiApplicationService;
    _propertyExtractor = propertyExtractor;
    _logger = logger;
    _activityFactory = activityFactory;
  }

  public IReadOnlyDictionary<string, IProxyCollection> UnpackSections(
    Collection rootCollection,
    string[] frameSectionNames,
    string[] shellSectionNames
  )
  {
    try
    {
      // Unpack frame sections
      var frameSections = UnpackFrameSections(frameSectionNames);
      if (frameSections.Count > 0)
      {
        rootCollection["frameSectionProxies"] = frameSections.Values.ToList();
      }

      // Unpack shell sections
      var shellSections = UnpackShellSections(shellSectionNames);
      if (shellSections.Count > 0)
      {
        rootCollection["shellSectionProxies"] = shellSections.Values.ToList();
      }

      // Return concatenated dictionary of both sections
      return frameSections.Concat(shellSections).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to unpack sections");
      return new Dictionary<string, IProxyCollection>();
    }
  }

  private Dictionary<string, IProxyCollection> UnpackFrameSections(string[] frameSectionNames)
  {
    Dictionary<string, IProxyCollection> sections = [];

    foreach (string frameSectionName in frameSectionNames)
    {
      try
      {
        SectionPropertyExtractionResult extractionResult = _propertyExtractor.ExtractFrameSectionProperties(
          frameSectionName
        );

        // TODO: Replace with SectionProxy when we've decided what to do here / when SDK updated
        GroupProxy proxy =
          new()
          {
            id = frameSectionName,
            name = frameSectionName,
            applicationId = frameSectionName,
            objects = [],
            ["Properties"] = extractionResult.Properties,
            ["MaterialName"] = extractionResult.MaterialName,
          };

        sections[frameSectionName] = proxy;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to extract frame section properties for {SectionName}", frameSectionName);
      }
    }

    return sections;
  }

  private Dictionary<string, IProxyCollection> UnpackShellSections(string[] shellSectionNames)
  {
    using var activity = _activityFactory.Start("Unpack Shell Sections");
    Dictionary<string, IProxyCollection> sections = [];

    foreach (string shellSectionName in shellSectionNames)
    {
      try
      {
        SectionPropertyExtractionResult extractionResult = _propertyExtractor.ExtractShellSectionProperties(
          shellSectionName
        );

        GroupProxy sectionProxy =
          new()
          {
            id = shellSectionName,
            name = shellSectionName,
            applicationId = shellSectionName,
            objects = [],
            ["Properties"] = extractionResult.Properties,
            ["MaterialName"] = extractionResult.MaterialName,
          };

        sections[shellSectionName] = sectionProxy;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to extract properties for shell section {SectionName}", shellSectionName);
      }
    }

    return sections;
  }
}
