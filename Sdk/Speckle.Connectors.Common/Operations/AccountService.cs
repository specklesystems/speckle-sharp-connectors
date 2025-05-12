using System.Runtime.Serialization;
using Speckle.InterfaceGenerator;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.SQLite;

namespace Speckle.Connectors.Common.Operations;

/// <summary>
/// Service that responsible to get account for DUI3 from account id otherwise from server url if any.
/// Note: Be sure it is registered on refactorings. Otherwise, we won't be able to do any send/receive ops.
/// This can safely be registered as singleton.
/// </summary>
[GenerateAutoInterface]
public class AccountService(
  IAccountManager accountManager,
  ISqLiteJsonCacheManagerFactory sqLiteJsonCacheManagerFactory
) : IAccountService
{
  /// <summary>
  /// Account to retrieve with its id, if not exist try to retrieve from matching serverUrl.
  /// </summary>
  /// <param name="accountId">Id of the account.</param>
  /// <param name="serverUrl">Server url to search matching account.</param>
  /// <returns></returns>
  /// <exception cref="SpeckleAccountManagerException"> Throws if server url doesn't match with any account.</exception>
  public Account GetAccountWithServerUrlFallback(string accountId, Uri serverUrl)
  {
    try
    {
      return accountManager.GetAccount(accountId);
    }
    catch (SpeckleAccountManagerException)
    {
      var accounts = accountManager.GetAccounts(serverUrl);
      return accounts.First()
        ?? throw new SpeckleAccountManagerException($"No any account found that matches with server {serverUrl}");
    }
  }

  public string? GetUserSelectedAccountId()
  {
    var jsonCacheManager = sqLiteJsonCacheManagerFactory.CreateForUser("DUI3Config");
    var rawConfig = jsonCacheManager.GetObject("accounts");
    if (rawConfig is null)
    {
      return null;
    }
    try
    {
      var config = JsonConvert.DeserializeObject<AccountsConfig>(rawConfig);
      if (config is null)
      {
        throw new SerializationException("Failed to deserialize accounts config");
      }

      return config.UserSelectedAccountId;
    }
    catch (SerializationException)
    {
      return null;
    }
  }

  public void SetUserSelectedAccountId(string userSelectedAccountId)
  {
    var jsonCacheManager = sqLiteJsonCacheManagerFactory.CreateForUser("DUI3Config");
    var str = JsonConvert.SerializeObject(new AccountsConfig() { UserSelectedAccountId = userSelectedAccountId });
    jsonCacheManager.UpdateObject("accounts", str);
  }
}

public class AccountsConfig
{
  public string? UserSelectedAccountId { get; set; }
}
