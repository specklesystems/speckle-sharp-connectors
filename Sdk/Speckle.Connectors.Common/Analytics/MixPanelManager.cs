using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Web;
using Microsoft.Extensions.Logging;
using Speckle.InterfaceGenerator;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Helpers;
#if NETFRAMEWORK
using System.Net.Http;
#endif

namespace Speckle.Connectors.Common.Analytics;

/// <summary>
///  Anonymous telemetry to help us understand how to make a better Speckle.
///  This really helps us to deliver a better open source project and product!
/// </summary>
[GenerateAutoInterface]
public class MixPanelManager(ISpeckleApplication application, ISpeckleHttp speckleHttp, ILogger<MixPanelManager> logger)
  : IMixPanelManager
{
  private const string MIXPANEL_TOKEN = "acd87c5a50b56df91a795e999812a3a4";
  private static readonly Uri s_mixpanelServer = new("https://analytics.speckle.systems");

  /// <summary>
  /// Cached email
  /// </summary>
  private string? LastEmail { get; set; }

  /// <summary>
  /// Cached server URL
  /// </summary>
  private string? LastServer { get; set; }

  /// <summary>
  /// <see langword="false"/> when the DEBUG pre-processor directive is <see langword="true"/>, <see langword="false"/> otherwise
  /// </summary>
  /// <remarks>This must be kept as a computed property, not a compile time const</remarks>
  private static bool IsReleaseMode =>
#if DEBUG
    false;
#else
    true;
#endif

  /// <summary>
  /// Tracks an event without specifying the email and server.
  /// It's not always possible to know which account the user has selected, especially in visual programming.
  /// Therefore we are caching the email and server values so that they can be used also when nodes such as "Serialize" are used.
  /// If no account info is cached, we use the default account data.
  /// </summary>
  /// <param name="eventName">Name of the even</param>
  /// <param name="customProperties">Additional parameters to pass in to event</param>
  /// <param name="isAction">True if it's an action performed by a logged user</param>
  public async Task TrackEvent(
    MixPanelEvents eventName,
    Account? account,
    Dictionary<string, object>? customProperties = null,
    bool isAction = true
  )
  {
    string? email = account?.userInfo.email;
    string? hashedEmail;
    string? server;

    if (LastEmail != null && LastServer != null && LastServer != "no-account-server")
    {
      hashedEmail = LastEmail;
      server = LastServer;
    }
    else
    {
      if (account == null)
      {
        var macAddr = NetworkInterface
          .GetAllNetworkInterfaces()
          .Where(nic =>
            nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
          )
          .Select(nic => nic.GetPhysicalAddress().ToString())
          .FirstOrDefault();

        hashedEmail = macAddr;
        server = "no-account-server";
        isAction = false;
      }
      else
      {
        hashedEmail = account.GetHashedEmail();
        server = account.GetHashedServer();
      }
    }

    await TrackEvent(hashedEmail, server, eventName, email, customProperties, isAction);
  }

  /// <summary>
  /// Tracks an event from a specified email and server, anonymizes personal information
  /// </summary>
  /// <param name="hashedEmail">Email of the user anonymized</param>
  /// <param name="hashedServer">Server URL anonymized</param>
  /// <param name="eventName">Name of the event</param>
  /// <param name="customProperties">Additional parameters to pass to the event</param>
  /// <param name="isAction">True if it's an action performed by a logged user</param>
  private async Task TrackEvent(
    string? hashedEmail,
    string hashedServer,
    MixPanelEvents eventName,
    string? email,
    Dictionary<string, object>? customProperties = null,
    bool isAction = true
  )
  {
    LastEmail = hashedEmail;
    LastServer = hashedServer;

    if (!IsReleaseMode)
    {
      //only track in prod
      return;
    }

    try
    {
      var properties = new Dictionary<string, object>
      {
        { "distinct_id", hashedEmail ?? string.Empty },
        { "server_id", hashedServer },
        { "token", MIXPANEL_TOKEN },
        { "hostApp", application.Slug },
        { "ui", "dui3" }, // this is the convention we use with next gen
        { "hostAppVersion", application.HostApplicationVersion },
        { "core_version", application.SpeckleVersion },
        { "$os", GetOs() }
      };

      if (email != null)
      {
        properties.Add("email", email);
      }

      if (isAction)
      {
        properties.Add("type", "action");
      }

      if (customProperties != null)
      {
        foreach (KeyValuePair<string, object> customProp in customProperties)
        {
          properties[customProp.Key] = customProp.Value;
        }
      }

      string json = JsonConvert.SerializeObject(new { @event = eventName.ToString(), properties });
      await SendAnalytics("/track?ip=1", json).ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(
        ex,
        "Analytics event {event} {isAction} failed {exceptionMessage}",
        eventName.ToString(),
        isAction,
        ex.Message
      );
    }
  }

  public async Task AddConnectorToProfile(string hashedEmail, string connector)
  {
    try
    {
      var data = new Dictionary<string, object>
      {
        { "$token", MIXPANEL_TOKEN },
        { "$distinct_id", hashedEmail },
        {
          "$union",
          new Dictionary<string, object>
          {
            {
              "Connectors",
              new List<string> { connector }
            }
          }
        }
      };
      string json = JsonConvert.SerializeObject(data);
      await SendAnalytics("/engage#profile-union", json).ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Failed add connector {connector} to profile", connector);
    }
  }

  public async Task IdentifyProfile(string hashedEmail, string connector)
  {
    try
    {
      var data = new Dictionary<string, object>
      {
        { "$token", MIXPANEL_TOKEN },
        { "$distinct_id", hashedEmail },
        {
          "$set",
          new Dictionary<string, object> { { "Identified", true } }
        }
      };
      string json = JsonConvert.SerializeObject(data);

      await SendAnalytics("/engage#profile-set", json).ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Failed identify profile: connector {connector}", connector);
    }
  }

  private async Task SendAnalytics(string relativeUri, string json)
  {
    var query = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("data=" + HttpUtility.UrlEncode(json))));
    using HttpClient client = speckleHttp.CreateHttpClient();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
    query.Headers.ContentType = new MediaTypeHeaderValue("application/json");
    var res = await client.PostAsync(new Uri(s_mixpanelServer, relativeUri), query).ConfigureAwait(false);
    res.EnsureSuccessStatusCode();
  }

  private static string GetOs()
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return "Windows";
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
      return "Mac OS X";
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      return "Linux";
    }

    return "Unknown";
  }
}
