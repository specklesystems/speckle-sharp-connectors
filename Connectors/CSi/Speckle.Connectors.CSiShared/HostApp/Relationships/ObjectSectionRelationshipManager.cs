using Microsoft.Extensions.Logging;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Relationships;

/// <summary>
/// Manages relationships between converted objects and their assigned sections.
/// </summary>
/// <remarks>
/// Handles relationships between objects and their section assignments.
/// Objects are pre-filtered by ICsiWrapper.RequiresSectionRelationship to ensure only
/// relevant objects are processed.
/// </remarks>
public class ObjectSectionRelationshipManager : IObjectSectionRelationshipManager
{
  private readonly ILogger<ObjectSectionRelationshipManager> _logger;

  public ObjectSectionRelationshipManager(ILogger<ObjectSectionRelationshipManager> logger)
  {
    _logger = logger;
  }

  public void EstablishRelationships(List<Base> convertedObjectsByType, List<IProxyCollection> sections)
  {
    foreach (var obj in convertedObjectsByType)
    {
      string? sectionName = GetObjectSectionName(obj);
      if (string.IsNullOrEmpty(sectionName))
      {
        continue;
      }

      var section = sections.FirstOrDefault(s => s.id == sectionName);
      if (section == null)
      {
        continue;
      }

      if (!section.objects.Contains(obj.applicationId!))
      {
        section.objects.Add(obj.applicationId!);
      }
    }
  }

  private string? GetObjectSectionName(Base baseObject)
  {
    // 🙍‍♂️ This below is horrible! I know. We need to refine the accessibility of sectionProperty in a more robust manner
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
