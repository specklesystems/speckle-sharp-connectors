using Microsoft.Extensions.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Relationships;

/// <summary>
/// Manages relationships between sections and materials.
/// </summary>
/// <remarks>
/// Establishes clear links between sections and materials with minimal coupling.
/// </remarks>
public class SectionMaterialRelationshipManager : ISectionMaterialRelationshipManager
{
  private readonly ILogger<SectionMaterialRelationshipManager> _logger;

  public SectionMaterialRelationshipManager(ILogger<SectionMaterialRelationshipManager> logger)
  {
    _logger = logger;
  }

  public void EstablishRelationships(
    IReadOnlyDictionary<string, IProxyCollection> sections,
    IReadOnlyDictionary<string, IProxyCollection> materials
  )
  {
    foreach (var section in sections.Values)
    {
      // This is critical that FrameSectionUnpacker and ShellSectionUnpacker extract material name exactly the same!
      // Maybe better to access materialId nested within properties? This "formalised" extraction result is not nice.
      var materialName = ((Base)section)["MaterialName"]?.ToString();
      if (materialName == null)
      {
        _logger.LogError($"Section {section.id} has no material name");
        continue;
      }

      if (!materials.TryGetValue(materialName, out var material))
      {
        _logger.LogError(
          $"Material {materialName} not found for section {section.id}. This indicates a conversion error"
        );
        continue;
      }

      if (material.objects.Contains(section.id!))
      {
        _logger.LogError($"No section should be processed twice. This is occuring for Section {section.id}");
        continue;
      }

      material.objects.Add(section.id!);
    }
  }
}
