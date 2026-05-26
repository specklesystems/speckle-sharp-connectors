using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.Collections;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Sdk;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Updates property values on Rhino objects via the user strings and user dictionary.
/// </summary>
public class RhinoPropertyUpdater
{
  private const string USER_DICTIONARY_KEY = "User Dictionary";

  private readonly ILogger<RhinoPropertyUpdater> _logger;

  public RhinoPropertyUpdater(ILogger<RhinoPropertyUpdater> logger)
  {
    _logger = logger;
  }

  public UpdateResult Update(RhinoObject rhinoObject, string[] pathParts, object? newValue)
  {
    if (pathParts.Length == 0)
    {
      return UpdateResult.Fail("Property path is empty");
    }

    try
    {
      var attributes = rhinoObject.Attributes.Duplicate();

      if (pathParts.Length == 1)
      {
        attributes.SetUserString(pathParts[0], newValue?.ToString() ?? string.Empty);
      }
      else if (pathParts[0] == USER_DICTIONARY_KEY)
      {
        if (pathParts.Length < 2)
        {
          return UpdateResult.Fail("'User Dictionary' path requires at least one key segment");
        }

        if (!SetNestedDictValue(attributes.UserDictionary, pathParts, 1, newValue))
        {
          return UpdateResult.Fail($"Failed to set User Dictionary path '{string.Join(".", pathParts)}'");
        }
      }
      else
      {
        return UpdateResult.Fail(
          $"Unsupported path '{string.Join(".", pathParts)}'. Supported: a single user string key, or 'User Dictionary.<key>'."
        );
      }

      if (!RhinoDoc.ActiveDoc.Objects.ModifyAttributes(rhinoObject, attributes, quiet: false))
      {
        return UpdateResult.Fail($"Rhino rejected attribute modification for object {rhinoObject.Id}");
      }

      return UpdateResult.Success();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to update property on object {ObjectId}", rhinoObject.Id);
      return UpdateResult.Fail($"Exception: {ex.Message}");
    }
  }

  /// <summary>
  /// Recursively navigates/creates intermediate ArchivableDictionary nodes, then sets the leaf value.
  /// Uses an index into the shared path array to avoid allocating sub-arrays on each recursion.
  /// Always writes intermediate nodes back so the change propagates regardless of copy/reference semantics.
  /// </summary>
  private bool SetNestedDictValue(ArchivableDictionary dict, string[] path, int index, object? newValue)
  {
    if (index >= path.Length)
    {
      return false;
    }

    if (index == path.Length - 1)
    {
      SetDictValue(dict, path[index], newValue);
      return true;
    }

    var intermediate =
      dict.ContainsKey(path[index]) && dict[path[index]] is ArchivableDictionary existing
        ? existing
        : new ArchivableDictionary();

    if (!SetNestedDictValue(intermediate, path, index + 1, newValue))
    {
      return false;
    }

    dict.Set(path[index], intermediate);
    return true;
  }

  private static void SetDictValue(ArchivableDictionary dict, string key, object? value)
  {
    if (value is null)
    {
      dict.Remove(key);
      return;
    }

    switch (value)
    {
      case bool b:
        dict.Set(key, b);
        break;
      case int i:
        dict.Set(key, i);
        break;
      case double d:
        dict.Set(key, d);
        break;
      default:
        dict.Set(key, value.ToString());
        break;
    }
  }
}
