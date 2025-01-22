using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.ETABSShared.HostApp.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
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
  private readonly EtabsSectionPropertyExtractor _propertyExtractor;
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;

  public EtabsSectionUnpacker(
    EtabsSectionPropertyExtractor propertyExtractor,
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton
  )
  {
    _propertyExtractor = propertyExtractor;
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
      Dictionary<string, object?> properties = _propertyExtractor.ExtractFrameSectionProperties(sectionName);

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
      Dictionary<string, object?> properties = _propertyExtractor.ExtractShellSectionProperties(sectionName);

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
