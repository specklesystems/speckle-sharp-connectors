using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Testing;

namespace Speckle.Connectors.Common.Tests.Operations;

public class SendOperationTests : MoqTest
{
#pragma warning disable CA1034
  [SpeckleType("TestBase")]
  public class TestBase : Base;
#pragma warning restore CA1034
  [Test]
#pragma warning disable CA1506
  public async Task Execute()
#pragma warning restore CA1506
  {
    var services = new ServiceCollection();
    services.AddSpeckleSdk(new("Tests", "tests"), "test", Assembly.GetExecutingAssembly());
    var rootObjectBuilder = Create<IRootObjectBuilder<object>>();
    var sendConversionCache = Create<ISendConversionCache>();
    var accountService = Create<IAccountService>();
    var sendProgress = Create<ISendProgress>();
    var operations = Create<IOperations>();
    var sendOperationVersionRecorder = Create<ISendOperationVersionRecorder>();
    var activityFactory = Create<ISdkActivityFactory>();
    var threadContext = Create<IThreadContext>();

    var ct = new CancellationToken();
    var objects = new List<object>();
    var projectId = "projectId";
    var sendInfo = new SendInfo(string.Empty, new Uri("https://localhost"), projectId, string.Empty, string.Empty);
    var progress = Create<IProgress<CardProgress>>();

    var conversionResults = new List<SendConversionResult>();
    var rootResult = new RootObjectBuilderResult(new TestBase(), conversionResults);
    rootObjectBuilder.Setup(x => x.Build(objects, projectId, progress.Object, ct)).ReturnsAsync(rootResult);

    var rootId = "rootId";
    var versionId = "versionId";
    var refs = new Dictionary<Id, ObjectReference>();
    var serializeProcessResults = new SerializeProcessResults(rootId, refs);
    threadContext
      .Setup(x => x.RunOnThreadAsync(It.IsAny<Func<Task<(SerializeProcessResults, string)>>>(), false))
      .ReturnsAsync((serializeProcessResults, versionId));

    var sp = services.BuildServiceProvider();

    var sendOperation = ActivatorUtilities.CreateInstance<SendOperation<object>>(
      sp,
      rootObjectBuilder.Object,
      sendConversionCache.Object,
      accountService.Object,
      sendProgress.Object,
      operations.Object,
      sendOperationVersionRecorder.Object,
      activityFactory.Object,
      threadContext.Object
    );
    var result = await sendOperation.Execute(objects, sendInfo, progress.Object, ct);
    result.Should().NotBeNull();
    rootResult.RootObject["version"].Should().Be(3);
    result.RootObjId.Should().Be(rootId);
    result.VersionId.Should().Be(versionId);
    result.ConvertedReferences.Should().BeSameAs(refs);
    result.ConversionResults.Should().BeSameAs(conversionResults);
  }

  [Test]
#pragma warning disable CA1506
  public async Task Send()
#pragma warning restore CA1506
  {
    var services = new ServiceCollection();
    services.AddSpeckleSdk(new("Tests", "tests"), "test", Assembly.GetExecutingAssembly());

    var rootObjectBuilder = Create<IRootObjectBuilder<object>>();
    var sendConversionCache = Create<ISendConversionCache>();
    var accountService = Create<IAccountService>();
    var sendProgress = Create<ISendProgress>();
    var operations = Create<IOperations>();
    var sendOperationVersionRecorder = Create<ISendOperationVersionRecorder>();
    var activityFactory = Create<ISdkActivityFactory>();
    var threadContext = Create<IThreadContext>();

    var commitObject = new TestBase();
    var projectId = "projectId";
    var modelId = "modelId";
    var accountId = "accountId";
    var url = new Uri("https://localhost");
    var sourceApplication = "sourceApplication";
    var sendInfo = new SendInfo(accountId, url, projectId, modelId, sourceApplication);
    var progress = Create<IProgress<CardProgress>>(MockBehavior.Loose);

    var ct = new CancellationToken();

    var token = "token";
    var account = new Account()
    {
      token = token,
      serverInfo = new ServerInfo() { url = url.ToString() }
    };
    var rootId = "rootId";
    var refs = new Dictionary<Id, ObjectReference>();
    var serializeProcessResults = new SerializeProcessResults(rootId, refs);
    accountService.Setup(x => x.GetAccountWithServerUrlFallback(accountId, url)).Returns(account);
    activityFactory.Setup(x => x.Start("SendOperation", "Send")).Returns((ISdkActivity?)null);

    operations
      .Setup(x => x.Send2(url, projectId, token, commitObject, It.IsAny<PassthroughProgress>(), ct))
      .ReturnsAsync(serializeProcessResults);

    sendConversionCache.Setup(x => x.StoreSendResult(projectId, refs));
    sendProgress.Setup(x => x.Begin());

    sendOperationVersionRecorder
      .Setup(x => x.RecordVersion(rootId, modelId, projectId, sourceApplication, account, ct))
      .ReturnsAsync("version");

    var sp = services.BuildServiceProvider();

    var sendOperation = ActivatorUtilities.CreateInstance<SendOperation<object>>(
      sp,
      rootObjectBuilder.Object,
      sendConversionCache.Object,
      accountService.Object,
      sendProgress.Object,
      operations.Object,
      sendOperationVersionRecorder.Object,
      activityFactory.Object,
      threadContext.Object
    );
    var (result, version) = await sendOperation.Send(commitObject, sendInfo, progress.Object, ct);
    result.Should().Be(serializeProcessResults);
    version.Should().Be("version");
  }
}
