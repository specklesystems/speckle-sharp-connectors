using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Common.Tests;

public class ServiceRegistrationTests
{
  private sealed class TestHostObjectBuilder : IHostObjectBuilder
  {
    public Task<HostObjectBuilderResult> Build(
      Base rootObject,
      string projectName,
      string modelName,
      IProgress<CardProgress> onOperationProgressed,
      CancellationToken cancellationToken
    ) => throw new NotImplementedException();
  }

  private sealed class TestThreadContext : IThreadContext
  {
    public bool IsMainThread { get; }

    public Task RunOnThread(Action action, bool useMain) => throw new NotImplementedException();

    public Task<T> RunOnThread<T>(Func<T> action, bool useMain) => throw new NotImplementedException();

    public Task RunOnThreadAsync(Func<Task> action, bool useMain) => throw new NotImplementedException();

    public Task<T> RunOnThreadAsync<T>(Func<Task<T>> action, bool useMain) => throw new NotImplementedException();
  }

  [Test]
  public void RegisterDependencies_Validation()
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddConnectors<TestHostObjectBuilder, TestThreadContext>(new("Tests", "test"), "v3");
    var serviceProvider = serviceCollection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    serviceProvider.Should().NotBeNull();
  }

  [Test]
  public void RegisterDependencies_Scopes()
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddConnectors<TestHostObjectBuilder, TestThreadContext>(new("Tests", "test"), "v3");
    var serviceProvider = serviceCollection.BuildServiceProvider(
      new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
    );
    serviceProvider.Should().NotBeNull();
  }
}
