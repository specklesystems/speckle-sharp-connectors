using Speckle.Connectors.DUI.Bridge;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.SQLite;

namespace Speckle.Connectors.DUI.Bindings;

public class AccountBinding : IBinding
{
  public string Name => "accountsBinding";
  public IBrowserBridge Parent { get; }

  private readonly IAccountManager _accountManager;
  private readonly ISqLiteJsonCacheManager _jsonCacheManager;

  public AccountBinding(
    IBrowserBridge bridge,
    IAccountManager accountManager,
    ISqLiteJsonCacheManagerFactory sqLiteJsonCacheManagerFactory
  )
  {
    Parent = bridge;
    _accountManager = accountManager;
    _jsonCacheManager = sqLiteJsonCacheManagerFactory.CreateForUser("Accounts");
  }

  public Account[] GetAccounts() => _accountManager.GetAccounts().ToArray();

  public void AddAccount(string accountId, Account account)
  {
    _jsonCacheManager.SaveObject(accountId, JsonConvert.SerializeObject(account));
  }

  public void RemoveAccount(string accountId) => _accountManager.RemoveAccount(accountId);
}
