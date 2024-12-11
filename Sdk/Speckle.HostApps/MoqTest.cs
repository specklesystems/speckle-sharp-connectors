using System.Diagnostics.CodeAnalysis;
using Moq;

namespace Speckle.HostApps;

[ExcludeFromCodeCoverage]
#pragma warning disable CA1012
#pragma warning disable CA1063
public abstract class MoqTest : IDisposable
#pragma warning restore CA1063
#pragma warning restore CA1012
{
  public MoqTest() => Repository = new(MockBehavior.Strict);
#pragma warning disable CA1816
#pragma warning disable CA1063
  public void Dispose() => Repository.VerifyAll();
#pragma warning restore CA1063
#pragma warning restore CA1816

  protected MockRepository Repository { get; private set; } = new(MockBehavior.Strict);

  protected Mock<T> Create<T>(MockBehavior behavior = MockBehavior.Strict)
    where T : class => Repository.Create<T>(behavior);
}
