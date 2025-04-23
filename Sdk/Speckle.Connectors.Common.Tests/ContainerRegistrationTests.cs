using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Speckle.Sdk;

namespace Speckle.Connectors.Common.Tests;

public class ServiceRegistrationTests
{
  [Test]
  public void RegisterDependencies_Validation()
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddSpeckleSdk(new("Tests", "test"), "v3");
    serviceCollection.AddConnectors();
    var serviceProvider = serviceCollection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    serviceProvider.Should().NotBeNull();
  }

  [Test]
  public void RegisterDependencies_Scopes()
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddSpeckleSdk(new("Tests", "test"), "v3");
    serviceCollection.AddConnectors();
    var serviceProvider = serviceCollection.BuildServiceProvider(
      new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
    );
    serviceProvider.Should().NotBeNull();
  }
}
