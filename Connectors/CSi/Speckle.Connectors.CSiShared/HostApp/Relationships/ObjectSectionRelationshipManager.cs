using Microsoft.Extensions.Logging;
using Speckle.Converters.CSiShared.Utils;
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

  public void EstablishRelationships(
    List<Base> convertedObjectsByType,
    IReadOnlyDictionary<string, IProxyCollection> sections
  )
  {
    foreach (var obj in convertedObjectsByType)
    {
      string? sectionName = GetObjectSectionName(obj);
      if (sectionName == null)
      {
        _logger.LogError($"No section name (sectionId) found for object {obj.applicationId}.");
        continue;
      }

      if (!sections.TryGetValue(sectionName, out var section))
      {
        continue; // This is valid. An opening has "none" for sectionId assignment. Not an error.
      }

      if (section.objects.Contains(obj.applicationId!))
      {
        _logger.LogError($"No object should be processed twice. This is occuring for Section {obj.applicationId}");
        continue;
      }

      section.objects.Add(obj.applicationId!);
    }
  }

  private string? GetObjectSectionName(Base baseObject)
  {
    // 🙍‍♂️ This below is horrible! Heavy use of dictionary-style property access is brittle
    // TODO: Make better :)
    try
    {
      if (baseObject["properties"] is not Dictionary<string, object?> properties)
      {
        return null;
      }

      if (
        !properties.TryGetValue(ObjectPropertyCategory.ASSIGNMENTS, out object? assignments)
        || assignments is not Dictionary<string, object?> assignmentsDict
      )
      {
        return null;
      }

      return assignmentsDict.TryGetValue("sectionId", out object? section) ? section?.ToString() : null;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to get section name for object {ObjectId}", baseObject.id);
      return null;
    }
  }
}
