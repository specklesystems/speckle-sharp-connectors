using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp;

/// <summary>
/// Base implementation for unpacking section properties common to all CSi products.
/// </summary>
/// <remarks>
/// Follows established project patterns:
/// - Type switching at shared level
/// - Delegation to type-specific extractors
/// - Virtual methods for application-specific customization
/// - Consistent proxy creation and property organization
/// </remarks>
public class SharedSectionUnpacker : ISectionUnpacker
{
  private readonly ILogger<SharedSectionUnpacker> _logger;
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly FrameSectionPropertiesUnpacker _frameSectionPropertiesUnpacker;

  public SharedSectionUnpacker(
    ILogger<SharedSectionUnpacker> logger,
    ICsiApplicationService csiApplicationService,
    ISdkActivityFactory activityFactory,
    FrameSectionPropertiesUnpacker frameSectionPropertiesUnpacker
  )
  {
    _logger = logger;
    _csiApplicationService = csiApplicationService;
    _activityFactory = activityFactory;
    _frameSectionPropertiesUnpacker = frameSectionPropertiesUnpacker;
  }

  public virtual List<IProxyCollection> UnpackSections(Collection rootObjectCollection)
  {
    try
    {
      using var activity = _activityFactory.Start("Unpack Sections");

      var frameSectionProxies = UnpackFrameSections();
      if (frameSectionProxies.Count > 0)
      {
        rootObjectCollection[ProxyKeys.SECTION] = frameSectionProxies;
      }

      // Future: Add other section types
      // var shellSectionProxies = UnpackShellSections();
      // if (shellSectionProxies.Count > 0)
      // {
      //     rootObjectCollection[ProxyKeys.SHELL_SECTION] = shellSectionProxies;
      // }

      return frameSectionProxies;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to unpack sections");
      return [];
    }
  }

  protected virtual List<IProxyCollection> UnpackFrameSections()
  {
    try
    {
      using var activity = _activityFactory.Start("Unpack Frame Sections");
      Dictionary<string, IProxyCollection> sections = [];

      // Get all sections
      int numberOfFrameSections = 0;
      string[] frameSectionNames = [];
      _csiApplicationService.SapModel.PropFrame.GetNameList(ref numberOfFrameSections, ref frameSectionNames);

      foreach (string frameSectionName in frameSectionNames)
      {
        try
        {
          string material = _frameSectionPropertiesUnpacker.GetMaterialName(frameSectionName);
          var properties = _frameSectionPropertiesUnpacker.GetProperties(frameSectionName);

          // 🫷 TODO: Scope a SectionProxy class? Below is a temp solution. GroupProxy in this context not quite right.
          GroupProxy sectionProxy =
            new()
            {
              id = frameSectionName,
              name = frameSectionName,
              applicationId = frameSectionName,
              objects = [],
              ["Properties"] = properties,
              ["MaterialName"] = material
            };

          sections[frameSectionName] = sectionProxy;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogError(ex, "Failed to extract properties for frame section {SectionName}", frameSectionName);
        }
      }

      return sections.Values.ToList();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to unpack frame sections");
      return [];
    }
  }
}
