using Speckle.InterfaceGenerator;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class AccountService(IAccountManager accountManager) : IAccountService
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
