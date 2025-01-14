using Microsoft.Extensions.Logging;
using Speckle.Converters.CSiShared.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp;

/// <summary>
/// Manages relationships between materials, sections, and converted objects.
/// </summary>
/// <remarks>
/// Operates after conversion to establish bidirectional relationships.
/// Handles both section-material and object-section relationships.
/// Assumes objects are pre-filtered by type for efficiency.
/// Uses object properties to determine section assignments.
/// Maintains relationship integrity through careful null-checking.
/// </remarks>
public class ProxyRelationshipManager : IProxyRelationshipManager
{
  private readonly ILogger<ProxyRelationshipManager> _logger;

  public ProxyRelationshipManager(ILogger<ProxyRelationshipManager> logger)
  {
    _logger = logger;
  }

  public void EstablishRelationships(
    Dictionary<string, List<Base>> convertedObjectsByType,
    List<IProxyCollection> materialProxies,
    List<IProxyCollection> sectionProxies
  )
  {
    EstablishSectionMaterialRelationships(sectionProxies, materialProxies);
    EstablishObjectSectionRelationships(convertedObjectsByType, sectionProxies);
  }

  private void EstablishSectionMaterialRelationships(
    List<IProxyCollection> sectionProxies,
    List<IProxyCollection> materialProxies
  )
  {
    foreach (var sectionProxy in sectionProxies)
    {
      var materialName = ((Base)sectionProxy)["MaterialName"]?.ToString(); // TODO: Fix when cleared up GroupProxy
      if (string.IsNullOrEmpty(materialName))
      {
        continue;
      }

      var materialProxy = materialProxies.FirstOrDefault(p => p.id == materialName);
      if (materialProxy == null)
      {
        continue;
      }

      if (!materialProxy.objects.Contains(sectionProxy.id!))
      {
        materialProxy.objects.Add(sectionProxy.id!);
      }
    }
  }

  private void EstablishObjectSectionRelationships(
    Dictionary<string, List<Base>> convertedObjectsByType,
    List<IProxyCollection> sectionProxies
  )
  {
    if (convertedObjectsByType.TryGetValue(ModelObjectType.FRAME.ToString(), out var frameObjects))
    {
      EstablishTypeObjectSectionRelationships(frameObjects, sectionProxies);
    }

    if (convertedObjectsByType.TryGetValue(ModelObjectType.SHELL.ToString(), out var shellObjects))
    {
      EstablishTypeObjectSectionRelationships(shellObjects, sectionProxies);
    }
  }

  private void EstablishTypeObjectSectionRelationships(List<Base> objects, List<IProxyCollection> sectionProxies)
  {
    foreach (var obj in objects)
    {
      string? sectionName = GetObjectSectionName(obj);
      if (string.IsNullOrEmpty(sectionName))
      {
        continue;
      }

      var sectionProxy = sectionProxies.FirstOrDefault(p => p.id == sectionName);
      if (sectionProxy == null)
      {
        continue;
      }

      if (!sectionProxy.objects.Contains(obj.applicationId!))
      {
        sectionProxy.objects.Add(obj.applicationId!);
      }
    }
  }

  private string? GetObjectSectionName(Base baseObject)
  {
    try
    {
      if (baseObject["properties"] is not Dictionary<string, object?> properties)
      {
        return null;
      }

      if (
        !properties.TryGetValue("Assignments", out object? assignments)
        || assignments is not Dictionary<string, object?> assignmentsDict
      )
      {
        return null;
      }

      return assignmentsDict.TryGetValue("sectionProperty", out object? section) ? section?.ToString() : null;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to get section name for object {ObjectId}", baseObject.id);
      return null;
    }
  }
}
