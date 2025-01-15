using Microsoft.Extensions.Logging;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.ETABSShared.HostApp.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.ETABSShared.HostApp.Sections;

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

  public List<IProxyCollection> UnpackSections(Collection rootCollection)
  {
    try
    {
      var frameSections = UnpackFrameSections();
      if (frameSections.Count > 0)
      {
        rootCollection["frameSectionProxies"] = frameSections;
      }

      var shellSections = UnpackShellSections();
      if (shellSections.Count > 0)
      {
        rootCollection["shellSectionProxies"] = shellSections;
      }

      return frameSections.Concat(shellSections).ToList();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to unpack sections");
      return [];
    }
  }

  private List<IProxyCollection> UnpackFrameSections()
  {
    Dictionary<string, IProxyCollection> sections = [];

    int numberOfSections = 0;
    string[] sectionNames = [];
    _csiApplicationService.SapModel.PropFrame.GetNameList(ref numberOfSections, ref sectionNames);

    foreach (string sectionName in sectionNames)
    {
      try
      {
        SectionPropertyExtractionResult extractionResult = _propertyExtractor.ExtractFrameSectionProperties(
          sectionName
        );

        // TODO: Replace with SectionProxy when we've decided what to do here / when SDK updated
        GroupProxy proxy =
          new()
          {
            id = sectionName,
            name = sectionName,
            applicationId = sectionName,
            objects = [],
            ["Properties"] = extractionResult.Properties,
            ["MaterialName"] = extractionResult.MaterialName,
          };

        sections[sectionName] = proxy;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to extract frame section properties for {SectionName}", sectionName);
      }
    }

    return sections.Values.ToList();
  }

  private List<IProxyCollection> UnpackShellSections()
  {
    using var activity = _activityFactory.Start("Unpack Shell Sections");
    Dictionary<string, IProxyCollection> sections = [];

    int numberOfAreaSections = 0;
    string[] areaPropertyNames = [];
    _csiApplicationService.SapModel.PropArea.GetNameList(ref numberOfAreaSections, ref areaPropertyNames);

    foreach (string areaPropertyName in areaPropertyNames)
    {
      try
      {
        SectionPropertyExtractionResult extractionResult = _propertyExtractor.ExtractShellSectionProperties(
          areaPropertyName
        );

        GroupProxy sectionProxy =
          new()
          {
            id = areaPropertyName,
            name = areaPropertyName,
            applicationId = areaPropertyName,
            objects = [],
            ["Properties"] = extractionResult.Properties,
            ["MaterialName"] = extractionResult.MaterialName,
          };

        sections[areaPropertyName] = sectionProxy;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to extract properties for shell section {SectionName}", areaPropertyName);
      }
    }

    return sections.Values.ToList();
  }
}
