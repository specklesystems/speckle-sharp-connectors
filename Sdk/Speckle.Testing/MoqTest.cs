using System.Diagnostics.CodeAnalysis;
using Moq;
using NUnit.Framework;

namespace Speckle.Testing;

[ExcludeFromCodeCoverage]
public abstract class MoqTest
{
  [SetUp]
  public void Setup() => Repository = new(MockBehavior.Strict);

  [TearDown]
  public void Verify() => Repository.VerifyAll();

  protected MockRepository Repository { get; private set; } = new(MockBehavior.Strict);

  protected Mock<T> Create<T>(MockBehavior behavior = MockBehavior.Strict)
    where T : class => Repository.Create<T>(behavior);
}
