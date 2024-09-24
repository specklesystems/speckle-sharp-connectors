using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

/// <summary>
/// Service that responsible to get account for DUI3 from account id otherwise from server url if any.
/// Note: Be sure it is registered on refactorings. Otherwise, we won't be able to do any send/receive ops.
/// This can safely be registered as singleton.
/// </summary>
public class AccountService(IAccountManager accountManager)
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
}
