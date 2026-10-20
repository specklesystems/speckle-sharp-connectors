using Microsoft.Extensions.Logging;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Settings;

[GenerateAutoInterface(VisibilityModifier = "public")]
internal sealed class GlobalConfigResolver(IGlobalConfigHelper helper, ILogger<IGlobalConfigResolver>? logger)
  : IGlobalConfigResolver
{
  public GlobalConfig GetConfig() =>
    new()
    {
      DefaultSpeckleServerUrl = GetDefaultSpeckleServerUrl(),
      DuiUrl = GetDuiUrl(),
      IsUpdateNotificationDisabled = GetIsUpdateNotificationDisabled(),
    };

  public Uri GetDefaultSpeckleServerUrl()
  {
    const string KEY = "SPECKLE_DEFAULT_SPECKLE_SERVER_URL";
    if (helper.GetConfigValue(KEY, out string? value))
    {
      if (Uri.TryCreate(value, UriKind.Absolute, out Uri? valid))
      {
        return valid;
      }
      logger?.LogWarning("Failed to parse {Key} '{Value}'", KEY, value);
    }

    return new Uri("https://app.speckle.systems", UriKind.Absolute);
  }

  public Uri GetDuiUrl()
  {
    const string KEY = "SPECKLE_DUI_URL";
    if (helper.GetConfigValue(KEY, out string? value))
    {
      if (Uri.TryCreate(value, UriKind.Absolute, out Uri? valid))
      {
        return valid;
      }
      logger?.LogWarning("Failed to parse {Key} '{Value}'", KEY, value);
    }

    return new Uri("https://dui.speckle.systems", UriKind.Absolute);
  }

  public bool GetIsUpdateNotificationDisabled()
  {
    const string KEY = "SPECKLE_IS_UPDATE_NOTIFICATION_DISABLED";
    if (helper.GetConfigValue(KEY, out string? value))
    {
      if (bool.TryParse(value, out bool boolean))
      {
        return boolean;
      }
      logger?.LogWarning("Failed to parse {Key} '{Value}'", KEY, value);
    }

    return false;
  }

  public bool Get()
  {
    const string KEY = "SPECKLE_IS_UPDATE_NOTIFICATION_DISABLED";
    if (helper.GetConfigValue(KEY, out string? value))
    {
      if (bool.TryParse(value, out bool boolean))
      {
        return boolean;
      }
      logger?.LogWarning("Failed to parse {Key} '{Value}'", KEY, value);
    }

    return false;
  }
}
