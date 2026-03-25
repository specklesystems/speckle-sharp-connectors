using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.SQLite;

namespace Speckle.Connectors.DUI.Bindings;

public sealed class AccountBinding(
  IBrowserBridge bridge,
  IAccountManager accountManager,
  ISqLiteJsonCacheManagerFactory sqLiteJsonCacheManagerFactory
) : IBinding, IDisposable
{
  public string Name => "accountsBinding";
  public IBrowserBridge Parent { get; } = bridge;

  private readonly ISqLiteJsonCacheManager _jsonCacheManager = sqLiteJsonCacheManagerFactory.CreateForUser("Accounts");
  private CancellationTokenSource _cancellationTokenSource = new();

  public Account[] GetAccounts() => accountManager.GetAccounts().ToArray();

  public void AddAccount(string accountId, Account account) =>
    _jsonCacheManager.SaveObject(accountId, JsonConvert.SerializeObject(account));

  public void RemoveAccount(string accountId) => accountManager.RemoveAccount(accountId);

  [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Binding API")]
  public async Task<Account> AuthenticateAccount(string serverUrl)
  {
    _cancellationTokenSource.Cancel();
    _cancellationTokenSource = new();

    return await accountManager.AuthenticateAccount(
      new Uri(serverUrl, UriKind.Absolute),
      TimeSpan.FromMinutes(5),
      _cancellationTokenSource.Token
    );
  }

  public void Dispose()
  {
    _cancellationTokenSource.Dispose();
    _jsonCacheManager.Dispose();
  }
}
