using Speckle.Connectors.DUI.Bridge;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.DUI.Bindings;

public class AccountBinding : IBinding
{
  public string Name => "accountsBinding";
  public IBridge Parent { get; }

  private readonly IAccountManager _accountManager;

  public AccountBinding(IBridge bridge, IAccountManager accountManager)
  {
    Parent = bridge;
    _accountManager = accountManager;
  }

  public Account[] GetAccounts() => _accountManager.GetAccounts().ToArray();
}
