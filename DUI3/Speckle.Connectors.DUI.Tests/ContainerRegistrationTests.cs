using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Models;
using Speckle.Sdk;

namespace Speckle.Connectors.DUI.Tests;

public class ServiceRegistrationTests
{
  [Test]
  public void RegisterDependencies_Validation()
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddSpeckleSdk(new("Tests", "test"), "v3");
    serviceCollection.AddDUI<DefaultThreadContext, DocumentModelStore>();
    var serviceProvider = serviceCollection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    serviceProvider.Should().NotBeNull();
  }

  [Test]
  public void RegisterDependencies_Scopes()
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddSpeckleSdk(new("Tests", "test"), "v3");
    serviceCollection.AddDUI<DefaultThreadContext, DocumentModelStore>();
    var serviceProvider = serviceCollection.BuildServiceProvider(
      new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
    );
    serviceProvider.Should().NotBeNull();
  }
}
