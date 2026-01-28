using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

[SuppressMessage(
  "Design",
  "CA1063:Implement IDisposable Correctly",
  Justification = "We're never going to need the full unmanaged disposal pattern hrere"
)]
[SuppressMessage(
  "Usage",
  "CA1816:Dispose methods should call SuppressFinalize",
  Justification = "We're never going to need the full unmanaged disposal pattern hrere"
)]
public record SendInfo(IClient Client, string ProjectId, string ModelId) : IDisposable
{
  public Account Account => Client.Account;

  public void Dispose() => Client.Dispose();
}
