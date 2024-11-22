using System.Runtime.Serialization;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.DUI.Bindings;

/// <summary>
/// POC: Simple config binding, as it was driving Dim nuts he couldn't swap to a dark theme.
/// How does it store configs? In a sqlite db called 'DUI3Config', we create a row for each host application:
/// [ hash,     contents         ]
/// ['Rhino',   serialised config]
/// ['Revit',   serialised config]
/// </summary>
public class ConfigBinding : IBinding
{
  public string Name => "configBinding";
  public IBrowserBridge Parent { get; }
  private SQLiteTransport ConfigStorage { get; }
  private readonly ISpeckleApplication _speckleApplication;
  private readonly IJsonSerializer _serializer;

  public ConfigBinding(IJsonSerializer serializer, ISpeckleApplication speckleApplication, IBrowserBridge bridge)
  {
    Parent = bridge;
    ConfigStorage = new SQLiteTransport(scope: "DUI3Config"); // POC: maybe inject? (if we ever want to use a different storage for configs later down the line)
    _speckleApplication = speckleApplication;
    _serializer = serializer;
  }

#pragma warning disable CA1024
  public bool GetIsDevMode()
#pragma warning restore CA1024
  {
#if DEBUG || LOCAL
    return true;
#else
    return false;
#endif
  }

  public async Task<ConnectorConfig> GetConfig()
  {
    var rawConfig = await ConfigStorage.GetObject(_speckleApplication.HostApplication).ConfigureAwait(false);
    if (rawConfig is null)
    {
      return SeedConfig();
    }

    try
    {
      var config = _serializer.Deserialize<ConnectorConfig>(rawConfig);
      if (config is null)
      {
        throw new SerializationException("Failed to deserialize config");
      }

      return config;
    }
    catch (SerializationException)
    {
      return SeedConfig();
    }
  }

  private ConnectorConfig SeedConfig()
  {
    var cfg = new ConnectorConfig();
    UpdateConfig(cfg);
    return cfg;
  }

  public void UpdateConfig(ConnectorConfig config)
  {
    var str = _serializer.Serialize(config);
    ConfigStorage.UpdateObject(_speckleApplication.HostApplication, str);
  }

  public void SetUserSelectedAccountId(string userSelectedAccountId)
  {
    var str = _serializer.Serialize(new AccountsConfig() { UserSelectedAccountId = userSelectedAccountId });
    ConfigStorage.UpdateObject("accounts", str);
  }

  public async Task<AccountsConfig?> GetUserSelectedAccountId()
  {
    var rawConfig = await ConfigStorage.GetObject("accounts").ConfigureAwait(false);
    if (rawConfig is null)
    {
      return null;
    }

    try
    {
      var config = _serializer.Deserialize<AccountsConfig>(rawConfig);
      if (config is null)
      {
        throw new SerializationException("Failed to deserialize accounts config");
      }

      return config;
    }
    catch (SerializationException)
    {
      return null;
    }
  }
}

/// <summary>
/// POC: A simple POCO for keeping track of settings. I see this as extensible in the future by each host application if and when we will need global per-app connector settings.
/// </summary>
public class ConnectorConfig
{
  public bool DarkTheme { get; set; } = true;
}

public class AccountsConfig
{
  public string? UserSelectedAccountId { get; set; }
}
