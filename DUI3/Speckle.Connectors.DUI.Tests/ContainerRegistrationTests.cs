using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.DUI.Tests;

public class ServiceRegistrationTests
{
  private sealed class TestDocumentModelStore(ILogger<DocumentModelStore> logger, IJsonSerializer serializer)
    : DocumentModelStore(logger, serializer)
  {
    protected override void HostAppSaveState(string modelCardState) => throw new NotImplementedException();

    protected override void LoadState() => throw new NotImplementedException();
  }

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

  private sealed class TestBrowserScriptExecutor : IBrowserScriptExecutor
  {
    public void ExecuteScript(string script) => throw new NotImplementedException();

    public void SendProgress(string script) => throw new NotImplementedException();

    public bool IsBrowserInitialized { get; }
    public object BrowserElement { get; }

    public void ShowDevTools() => throw new NotImplementedException();
  }

  [Test]
  public void RegisterDependencies_Validation()
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddDUISendReceive<TestDocumentModelStore, TestHostObjectBuilder, TestThreadContext>(
      new("Tests", "test"),
      HostAppVersion.v3
    );
    serviceCollection.AddSingleton<IBrowserScriptExecutor, TestBrowserScriptExecutor>();
    var serviceProvider = serviceCollection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    serviceProvider.Should().NotBeNull();
  }

  [Test]
  public void RegisterDependencies_Scopes()
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddDUISendReceive<TestDocumentModelStore, TestHostObjectBuilder, TestThreadContext>(
      new("Tests", "test"),
      HostAppVersion.v3
    );
    serviceCollection.AddSingleton<IBrowserScriptExecutor, TestBrowserScriptExecutor>();
    var serviceProvider = serviceCollection.BuildServiceProvider(
      new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
    );
    serviceProvider.Should().NotBeNull();
  }
}
