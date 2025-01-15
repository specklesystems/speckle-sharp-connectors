using Microsoft.Extensions.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Relationships;

/// <summary>
/// Manages relationships between sections and their assigned materials.
/// </summary>
/// <remarks>
/// Handles only section-material relationships for clear separation of concerns.
/// Uses material names from section properties to establish links.
/// Performs null checks and logging to maintain relationship integrity.
/// </remarks>
public class SectionMaterialRelationshipManager : ISectionMaterialRelationshipManager
{
  private readonly ILogger<SectionMaterialRelationshipManager> _logger;

  public SectionMaterialRelationshipManager(ILogger<SectionMaterialRelationshipManager> logger)
  {
    _logger = logger;
  }

  public void EstablishRelationships(List<IProxyCollection> sections, List<IProxyCollection> materials)
  {
    foreach (var section in sections)
    {
      // This is critical that FrameSectionUnpacker and ShellSectionUnpacker extract material name exactly the same!
      var materialName = ((Base)section)["MaterialName"]?.ToString();
      if (string.IsNullOrEmpty(materialName))
      {
        continue;
      }

      var material = materials.FirstOrDefault(m => m.id == materialName);
      if (material == null)
      {
        continue;
      }

      if (!material.objects.Contains(section.id!))
      {
        material.objects.Add(section.id!);
      }
    }
  }
}
