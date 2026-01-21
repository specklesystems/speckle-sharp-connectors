using System.Runtime.Serialization;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Utils;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.SQLite;

namespace Speckle.Connectors.DUI.Settings;

/// <summary>
/// See <see cref="ConfigBinding"/>, as it was driving Dim nuts he couldn't swap to a dark theme.
/// How does it store configs? In a sqlite db called 'DUI3Config', we create a row for each host application:
/// [ hash,     contents         ]
/// ['Rhino',   serialised config]
/// ['Revit',   serialised config]
/// </summary>
/// <remarks>
/// We separated the business logic that's in this class from the <see cref="ConfigBinding"/> so that
/// <see cref="ConfigStore"/> can be injected into other bindings (you can't inject one binding into another)
/// </remarks>
[GenerateAutoInterface]
public sealed class ConfigStore : IConfigStore
{
  private readonly ISqLiteJsonCacheManager _jsonCacheManager;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly IJsonSerializer _serializer;

  public ConfigStore(
    IJsonSerializer serializer,
    ISpeckleApplication speckleApplication,
    ISqLiteJsonCacheManagerFactory sqLiteJsonCacheManagerFactory
  )
  {
    _jsonCacheManager = sqLiteJsonCacheManagerFactory.CreateForUser("DUI3Config"); // POC: maybe inject? (if we ever want to use a different storage for configs later down the line)
    _speckleApplication = speckleApplication;
    _serializer = serializer;
  }

  public ConnectorConfig GetConnectorConfig()
  {
    var rawConfig = _jsonCacheManager.GetObject(_speckleApplication.HostApplication);
    if (rawConfig is null)
    {
      return SeedConnectorConfig();
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
      return SeedConnectorConfig();
    }
  }

  private ConnectorConfig SeedConnectorConfig()
  {
    var cfg = new ConnectorConfig();
    UpdateConnectorConfig(cfg);
    return cfg;
  }

  public void UpdateConnectorConfig(ConnectorConfig config)
  {
    var str = _serializer.Serialize(config);
    _jsonCacheManager.UpdateObject(_speckleApplication.HostApplication, str);
  }

  public void UpdateAccountConfig(AccountsConfig accountsConfig)
  {
    var str = _serializer.Serialize(accountsConfig);
    _jsonCacheManager.UpdateObject("accounts", str);
  }

  public GlobalConfig? GetGlobalConfig()
  {
    var rawConfig = _jsonCacheManager.GetObject("global");
    if (rawConfig is null)
    {
      return null;
    }

    try
    {
      var config = _serializer.Deserialize<GlobalConfig>(rawConfig);
      if (config is null)
      {
        throw new SerializationException("Failed to deserialize global config");
      }

      return config;
    }
    catch (SerializationException)
    {
      return null;
    }
  }

  public AccountsConfig? GetAccountsConfig()
  {
    var rawConfig = _jsonCacheManager.GetObject("accounts");
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

  public WorkspacesConfig? GetWorkspacesConfig()
  {
    var rawConfig = _jsonCacheManager.GetObject("workspaces");
    if (rawConfig is null)
    {
      return null;
    }

    try
    {
      var config = _serializer.Deserialize<WorkspacesConfig>(rawConfig);
      if (config is null)
      {
        throw new SerializationException("Failed to deserialize workspaces config");
      }

      return config;
    }
    catch (SerializationException)
    {
      return null;
    }
  }

  public void UpdateWorkspacesConfig(WorkspacesConfig workspacesConfig)
  {
    var str = _serializer.Serialize(workspacesConfig);
    _jsonCacheManager.UpdateObject("workspaces", str);
  }
}

/// <summary>
/// POC: A simple POCO for keeping track of settings. I see this as extensible in the future by each host application if and when we will need global per-app connector settings.
/// </summary>
public sealed class ConnectorConfig
{
  public bool DarkTheme { get; init; } = true;

  /// <remarks>
  /// Only used by Revit Connector !!
  /// We're exposing some settings to disable event listening inorder to debug app crash issues caused by Revit event handlers
  /// Normal users are expected to have both enabled
  /// </remarks>
  public bool SelectionChangeListeningDisabled { get; init; }

  /// <inheritdoc cref="SelectionChangeListeningDisabled" />
  public bool DocumentChangeListeningDisabled { get; init; }
}

public sealed class GlobalConfig
{
  public bool IsUpdateNotificationDisabled { get; init; }
}

public sealed class AccountsConfig
{
  public string? UserSelectedAccountId { get; init; }
}

public sealed class WorkspacesConfig
{
  public string? UserSelectedWorkspaceId { get; init; }
}
