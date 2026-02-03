using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.Logging;

namespace Speckle.Connectors.DUI.Bindings;

/// <summary>
/// POC: Simple config binding, as it was driving Dim nuts he couldn't swap to a dark theme.
/// How does it store configs? In a sqlite db called 'DUI3Config', we create a row for each host application:
/// [ hash,     contents         ]
/// ['Rhino',   serialised config]
/// ['Revit',   serialised config]
/// </summary>
public class ConfigBinding(IConfigStore configStore, IBrowserBridge bridge) : IBinding
{
  public string Name => "configBinding";
  public IBrowserBridge Parent { get; } = bridge;

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

  public ConnectorConfig GetConfig() => configStore.GetConnectorConfig();

  public void UpdateConfig(ConnectorConfig config) => configStore.UpdateConnectorConfig(config);

  public void SetUserSelectedAccountId(string userSelectedAccountId)
  {
    var config = new AccountsConfig() { UserSelectedAccountId = userSelectedAccountId };
    configStore.UpdateAccountConfig(config);
  }

  // TODO: need to be replaced with `GetAccountsConfig` function after some amount of time to not confuse ourselves.
  public AccountsConfig? GetUserSelectedAccountId() => GetAccountsConfig();

  public GlobalConfig? GetGlobalConfig() => configStore.GetGlobalConfig();

  public AccountsConfig? GetAccountsConfig() => configStore.GetAccountsConfig();

  public void SetUserSelectedWorkspaceId(string workspaceId)
  {
    var config = new WorkspacesConfig() { UserSelectedWorkspaceId = workspaceId };
    configStore.UpdateWorkspacesConfig(config);
  }

  public WorkspacesConfig? GetWorkspacesConfig() => configStore.GetWorkspacesConfig();

  [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Expose to UI")]
  [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Expose to UI")]
  public string GetSessionId() => Consts.StaticSessionId;
}
