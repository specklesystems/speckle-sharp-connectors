using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Logging;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Helpers;
#if NETFRAMEWORK
using System.Net.Http;
#endif

namespace Speckle.Connectors.Common.Analytics;

public interface IPostHogManager
{
  Task TrackEvent(
    AnalyticsEvent eventName,
    Account account,
    string? workspaceId,
    IReadOnlyDictionary<string, object>? customProperties = null,
    [CallerMemberName] string callerName = ""
  );
}

/// <summary>
///  Lightweight Telemetry to help us understand how to make a better Speckle.
/// </summary>
public class PostHogManager : IPostHogManager
{
  private readonly ISpeckleApplication _application;
  private readonly ISpeckleHttp _speckleHttp;
  private readonly ILogger<PostHogManager> _logger;
  private readonly Version _sdkVersion;

  /// <summary>
  ///  Lightweight Telemetry to help us understand how to make a better Speckle.
  /// </summary>
  public PostHogManager(ISpeckleApplication application, ISpeckleHttp speckleHttp, ILogger<PostHogManager> logger)
  {
    _application = application;
    _speckleHttp = speckleHttp;
    _logger = logger;

    string[] semverBrake = _application.SpeckleVersion.Split('-');
    _sdkVersion = Version.Parse(semverBrake.First());
  }

  private const string PRODUCT_TOKEN = "phc_7zaDwBgrBYb1yUe0Ff3Sn0DUibq0NoPNxYNC90M7cfg";
  private static readonly Uri s_posthogServer = new("https://eu.i.posthog.com");
  private static readonly Uri s_endpoint = new("/i/v0/e/", UriKind.Relative);

  /// <summary>
  /// <see langword="false"/> when the DEBUG pre-processor directive is <see langword="true"/>, <see langword="false"/> otherwise
  /// </summary>
  /// <remarks>This must be kept as a computed property, not a compile time const to satisfy dead code inspections</remarks>
  private static bool IsReleaseMode =>
#if DEBUG
    false;
#else
    true;
#endif

  public async Task TrackEvent(
    AnalyticsEvent eventName,
    Account account,
    string? workspaceId,
    IReadOnlyDictionary<string, object>? customProperties = null,
    [CallerMemberName] string callerName = ""
  )
  {
    // Right now, we're keeping posthog only for app.speckle.systems users
    if (new Uri(account.serverInfo.url) != new Uri("https://app.speckle.systems"))
    {
      return;
    }

    if (string.IsNullOrWhiteSpace(account.userInfo.email))
    {
      throw new ArgumentException("Email cannot be empty.", nameof(account));
    }

    var hashedServer = account.GetHashedServer();
    string distinctId = account.userInfo.id;
    try
    {
      var properties = new Dictionary<string, object?>
      {
        { "$os", GetOs() },
        { "$os_version", Environment.OSVersion.Version.ToString() },
        { "$lib", "Speckle.Sdk" },
        { "$lib_version", Consts.GetPackageVersion(typeof(PostHogManager).Assembly) },
        { "$user_id", distinctId },
        { "$session_id", Consts.StaticSessionId },
        { "$host", new Uri(account.serverInfo.url).Host },
        { "serverId", hashedServer },
        { "hostAppSlug", _application.Slug },
        { "hostAppVersion", _application.HostApplicationVersion },
        { "connectorVersion", _application.SpeckleVersion },
        { "connectorVersionMajor", _sdkVersion.Major },
        { "connectorVersionMinor", _sdkVersion.Minor },
        { "connectorVersionPatch", _sdkVersion.Build },
        { "osDescription", RuntimeInformation.OSDescription },
        { "dotnetRuntime", RuntimeInformation.FrameworkDescription },
        { "callerName", callerName },
#if NET5_0_OR_GREATER
        { "runtimeIdentifier", RuntimeInformation.RuntimeIdentifier },
#endif
        { "email", account.userInfo.email },
        { "workspace_id", workspaceId },
      };

      if (customProperties != null)
      {
        foreach (KeyValuePair<string, object> customProp in customProperties)
        {
          properties[customProp.Key] = customProp.Value;
        }
      }

      string json = JsonConvert.SerializeObject(
        new
        {
          api_key = PRODUCT_TOKEN,
          @event = eventName.ToString(),
          distinct_id = distinctId,
          timestamp = DateTimeOffset.UtcNow,
          properties,
        }
      );
      await SendAnalytics(json).ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Analytics event {Event} failed {ExceptionMessage}", eventName.ToString(), ex.Message);
    }
  }

  private async Task SendAnalytics(string json)
  {
    if (!IsReleaseMode)
    {
      //only track in prod, do this last to make it hard for bugs to creep into all other logic.
      return;
    }

    var query = new StringContent(json, Encoding.UTF8, "application/json");
    using HttpClient client = _speckleHttp.CreateHttpClient();
    client.BaseAddress = s_posthogServer;
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    var res = await client.PostAsync(s_endpoint, query).ConfigureAwait(false);
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
