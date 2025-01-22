using Microsoft.Extensions.Logging;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.ETABSShared.HostApp.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
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
  // A cache storing a map of section name <-> objects ids using this section
  public Dictionary<string, List<string>> SectionCache { get; set; } = new();

  private readonly ICsiApplicationService _csiApplicationService;
  private readonly EtabsSectionPropertyExtractor _propertyExtractor;
  private readonly ILogger<EtabsSectionUnpacker> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;

  public EtabsSectionUnpacker(
    ICsiApplicationService csiApplicationService,
    EtabsSectionPropertyExtractor propertyExtractor,
    ILogger<EtabsSectionUnpacker> logger,
    ISdkActivityFactory activityFactory,
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton
  )
  {
    _csiApplicationService = csiApplicationService;
    _propertyExtractor = propertyExtractor;
    _logger = logger;
    _activityFactory = activityFactory;
    _csiToSpeckleCacheSingleton = csiToSpeckleCacheSingleton;
  }

  public IEnumerable<GroupProxy> UnpackSections()
  {
    foreach (GroupProxy frameSectionProxy in UnpackFrameSections())
    {
      yield return frameSectionProxy;
    }

    foreach (GroupProxy shellSectionProxy in UnpackShellSections())
    {
      yield return shellSectionProxy;
    }
  }

  private IEnumerable<GroupProxy> UnpackFrameSections()
  {
    foreach (var entry in _csiToSpeckleCacheSingleton.FrameSectionCache)
    {
      string sectionName = entry.Key;
      List<string> frameIds = entry.Value;

      // get the properties of the section
      // TODO: add dictionaries directly and remove extraction result class
      Dictionary<string, object?> properties = new();
      _propertyExtractor.ExtractProperties(sectionName, properties);

      // create the section proxy
      GroupProxy sectionProxy =
        new()
        {
          id = sectionName,
          name = sectionName,
          applicationId = sectionName,
          objects = frameIds,
          ["Properties"] = properties
        };

      yield return sectionProxy;
    }
  }

  private IEnumerable<GroupProxy> UnpackShellSections()
  {
    foreach (var entry in _csiToSpeckleCacheSingleton.ShellSectionCache)
    {
      string sectionName = entry.Key;
      List<string> frameIds = entry.Value;

      // get the properties of the section
      // TODO: add dictionaries directly and remove extraction result class
      Dictionary<string, object?> properties = new();
      _propertyExtractor.ExtractShellSectionProperties(sectionName, properties);

      // create the section proxy
      GroupProxy sectionProxy =
        new()
        {
          id = sectionName,
          name = sectionName,
          applicationId = sectionName,
          objects = frameIds,
          ["Properties"] = properties
        };

      yield return sectionProxy;
    }
  }
}
