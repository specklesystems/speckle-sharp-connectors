using Speckle.Connectors.Logging;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Extensions;

internal sealed class AggregateIDisposable(IDisposable[] disposables) : IDisposable
{
  public void Dispose()
  {
    foreach (var disposable in disposables)
    {
      disposable?.Dispose();
    }
  }
}

public static class UserActivityScope
{
  public static IDisposable AddUserScope(Account account)
  {
    return new AggregateIDisposable(
      [
        ActivityScope.SetTag(Consts.USER_ID, account.userInfo.id),
        ActivityScope.SetTag(Consts.USER_DISTINCT_ID, account.GetHashedEmail()),
        ActivityScope.SetTag(Consts.USER_SERVER_URL, new Uri(account.serverInfo.url).ToString()),
      ]
    );
  }
}
