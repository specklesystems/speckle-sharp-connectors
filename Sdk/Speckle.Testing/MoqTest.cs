using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Speckle.Sdk;

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

  protected IServiceCollection CreateServices(params Assembly[] assemblies)
  {
    var services = new ServiceCollection();
    services.AddSpeckleSdk(new("Tests", "tests"), "test", assemblies);
    return services;
  }
}
