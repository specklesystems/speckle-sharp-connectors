using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Testing;

namespace Speckle.Connectors.Common.Tests.Operations;

public class ReceiveOperationTests : MoqTest
{
#pragma warning disable CA1034
  [SpeckleType("TestBase")]
  public class TestBase : Base;
#pragma warning restore CA1034
  [Test]
  public async Task Execute()
  {
    var hostObjectBuilder = Create<IHostObjectBuilder>();
    var receiveProgress = Create<IReceiveProgress>();
    var operations = Create<IOperations>();
    var receiveVersionRetriever = Create<IReceiveVersionRetriever>();
    var activityFactory = Create<ISdkActivityFactory>(MockBehavior.Loose);
    var threadContext = Create<IThreadContext>();

    var @base = new TestBase();
    var account = new Account();
    var version = new Speckle.Sdk.Api.GraphQL.Models.Version();
    var projectName = "projectName";
    var modelName = "modelName";

    var ct = new CancellationToken();
    var receiveInfo = new ReceiveInfo(
      account,
      string.Empty,
      projectName,
      string.Empty,
      modelName,
      string.Empty,
      string.Empty
    );
    var progress = Create<IProgress<CardProgress>>();

    var hostResult = new HostObjectBuilderResult([], []);

    receiveVersionRetriever.Setup(x => x.GetVersion(account, receiveInfo, ct)).ReturnsAsync(version);
    receiveVersionRetriever
      .Setup(x => x.VersionReceived(account, version, receiveInfo, ct))
      .Returns(Task.CompletedTask);
    hostObjectBuilder.Setup(x => x.Build(@base, projectName, modelName, progress.Object, ct)).ReturnsAsync(hostResult);

    threadContext.Setup(x => x.RunOnThreadAsync(It.IsAny<Func<Task<Base>>>(), false)).ReturnsAsync(@base);

    var sp = CreateServices(Assembly.GetExecutingAssembly()).BuildServiceProvider();
    var receiveOperation = ActivatorUtilities.CreateInstance<ReceiveOperation>(
      sp,
      hostObjectBuilder.Object,
      receiveProgress.Object,
      activityFactory.Object,
      operations.Object,
      receiveVersionRetriever.Object,
      threadContext.Object
    );
    var result = await receiveOperation.Execute(receiveInfo, progress.Object, ct);
    result.Should().Be(hostResult);
  }

  [Test]
  public async Task ReceiveData()
  {
    var hostObjectBuilder = Create<IHostObjectBuilder>();
    var receiveProgress = Create<IReceiveProgress>();
    var operations = Create<IOperations>();
    var receiveVersionRetriever = Create<IReceiveVersionRetriever>();
    var activityFactory = Create<ISdkActivityFactory>(MockBehavior.Loose);
    var threadContext = Create<IThreadContext>();

    var @base = new TestBase();
    var token = "token";
    var serverUrl = new Uri("https://localhost");
    var projectId = "projectId";
    var account = new Account()
    {
      token = token,
      serverInfo = new ServerInfo() { url = serverUrl.ToString() }
    };
    string referencedObject = "referencedObject";
    var version = new Speckle.Sdk.Api.GraphQL.Models.Version() { referencedObject = referencedObject };

    var ct = new CancellationToken();
    var receiveInfo = new ReceiveInfo(
      account,
      projectId,
      string.Empty,
      string.Empty,
      string.Empty,
      string.Empty,
      string.Empty
    );
    var progress = Create<IProgress<CardProgress>>();

    receiveProgress.Setup(x => x.Begin());
    operations
      .Setup(x => x.Receive2(serverUrl, projectId, referencedObject, token, It.IsAny<PassthroughProgress>(), ct))
      .ReturnsAsync(@base);

    var sp = CreateServices(Assembly.GetExecutingAssembly()).BuildServiceProvider();
    var receiveOperation = ActivatorUtilities.CreateInstance<ReceiveOperation>(
      sp,
      hostObjectBuilder.Object,
      receiveProgress.Object,
      activityFactory.Object,
      operations.Object,
      receiveVersionRetriever.Object,
      threadContext.Object
    );
    var result = await receiveOperation.ReceiveData(account, version, receiveInfo, progress.Object, ct);
    result.Should().Be(@base);
  }
}
