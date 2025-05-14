using System.Text.RegularExpressions;
using Speckle.Sdk;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

public record SpeckleResourceBuilder
{
  /// <summary>
  /// The ReGex pattern to determine if a URL's AbsolutePath is a Frontend2 URL or not.
  /// </summary>
  private static readonly Regex s_fe2UrlRegex =
    new(
      @"/projects/(?<projectId>[\w\d]+)(?:/models/(?<model>[\w\d]+(?:@[\w\d]+)?)(?:,(?<additionalModels>[\w\d]+(?:@[\w\d]+)?))*)?"
    );

  public static SpeckleUrlModelResource[] FromUrlString(string speckleModel)
  {
    var uri = new Uri(speckleModel);
    var serverUrl = uri.GetLeftPart(UriPartial.Authority);
    var match = s_fe2UrlRegex.Match(speckleModel);
    var result = ParseFe2RegexMatch(serverUrl, match);
    return result;
  }

  private static SpeckleUrlModelResource[] ParseFe2RegexMatch(string serverUrl, Match match)
  {
    var projectId = match.Groups["projectId"];
    var model = match.Groups["model"];
    var additionalModels = match.Groups["additionalModels"];

    if (!projectId.Success)
    {
      throw new SpeckleException("The provided url is not a valid Speckle url");
    }

    if (!model.Success)
    {
      throw new SpeckleException("The provided url is not pointing to any model in the project.");
    }

    if (model.Value == "all")
    {
      throw new NotSupportedException("Fetching all models is not supported.");
    }

    if (model.Value.StartsWith("$"))
    {
      throw new NotSupportedException("Federation model urls are not supported");
    }

    var modelRes = GetUrlModelResource(null, serverUrl, projectId.Value, model.Value);

    var result = new List<SpeckleUrlModelResource> { modelRes };

    if (additionalModels.Success)
    {
      foreach (Capture additionalModelsCapture in additionalModels.Captures)
      {
        var extraModel = GetUrlModelResource(null, serverUrl, projectId.Value, additionalModelsCapture.Value);
        result.Add(extraModel);
      }
    }

    return result.ToArray();
  }

  private static SpeckleUrlModelResource GetUrlModelResource(
    string? accountId,
    string serverUrl,
    string projectId,
    string modelValue
  )
  {
    if (modelValue.Length == 32)
    {
      return new SpeckleUrlModelObjectResource(accountId, serverUrl, projectId, modelValue); // Model value is an ObjectID
    }

    if (!modelValue.Contains('@'))
    {
      return new SpeckleUrlLatestModelVersionResource(accountId, serverUrl, projectId, modelValue); // Model has no version attached
    }

    var res = modelValue.Split('@');
    return new SpeckleUrlModelVersionResource(accountId, serverUrl, projectId, res[0], res[1]);
  }
}
