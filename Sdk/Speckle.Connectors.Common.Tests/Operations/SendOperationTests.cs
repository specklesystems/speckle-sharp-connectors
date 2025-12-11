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
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Testing;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.Common.Tests.Operations;

public class SendOperationTests : MoqTest
{
  [SpeckleType("TestBase")]
  public class TestBase : Base;

  [Test]
#pragma warning disable CA1506 // Avoid excessive class coupling
  public async Task Execute()
#pragma warning restore CA1506
  {
    var services = new ServiceCollection();
    services.AddSpeckleSdk(new("Tests", "tests"), "test", Assembly.GetExecutingAssembly());
    var rootObjectBuilder = Create<IRootObjectBuilder<object>>();
    var sendConversionCache = Create<ISendConversionCache>();
    var sendProgress = Create<ISendProgress>();
    var sendOperationExecutor = Create<ISendOperationExecutor>();
    var sendOperationVersionRecorder = Create<ISendOperationVersionRecorder>();
    var activityFactory = Create<ISdkActivityFactory>();
    var threadContext = Create<IThreadContext>();

    var ct = new CancellationToken();
    var objects = new List<object>();
    var projectId = "projectId";
    var sendInfo = new SendInfo(new Account(), projectId, string.Empty, string.Empty);
    var progress = Create<IProgress<CardProgress>>();

    var conversionResults = new List<SendConversionResult>();
    var rootResult = new RootObjectBuilderResult(new TestBase(), conversionResults);
    rootObjectBuilder.Setup(x => x.Build(objects, projectId, progress.Object, ct)).ReturnsAsync(rootResult);

    var rootId = "rootId";
    var versionId = "versionId";
    var refs = new Dictionary<Id, ObjectReference>();
    var serializeProcessResults = new SerializeProcessResults(rootId, refs);
    threadContext
      .Setup(x => x.RunOnThreadAsync(It.IsAny<Func<Task<(SerializeProcessResults, Version)>>>(), false))
      .ReturnsAsync((serializeProcessResults, new Version() { id = versionId }));

    var sp = services.BuildServiceProvider();

    var sendOperation = ActivatorUtilities.CreateInstance<SendOperation<object>>(
      sp,
      rootObjectBuilder.Object,
      sendConversionCache.Object,
      sendProgress.Object,
      sendOperationExecutor.Object,
      sendOperationVersionRecorder.Object,
      activityFactory.Object,
      threadContext.Object
    );
    var result = await sendOperation.Execute(objects, sendInfo, null, progress.Object, ct);
    result.Should().NotBeNull();
    rootResult.RootObject["version"].Should().Be(3);
    result.RootObjId.Should().Be(rootId);
    result.VersionId.Should().Be(versionId);
    result.ConvertedReferences.Should().BeSameAs(refs);
    result.ConversionResults.Should().BeSameAs(conversionResults);
  }

  [Test]
#pragma warning disable CA1506 // Avoid excessive class coupling
  public async Task Send()
#pragma warning restore CA1506
  {
    var services = new ServiceCollection();
    services.AddSpeckleSdk(new("Tests", "tests"), "test", Assembly.GetExecutingAssembly());

    var rootObjectBuilder = Create<IRootObjectBuilder<object>>();
    var sendConversionCache = Create<ISendConversionCache>();
    var sendProgress = Create<ISendProgress>();
    var sendOperationExecutor = Create<ISendOperationExecutor>();
    var activityFactory = Create<ISdkActivityFactory>();
    var threadContext = Create<IThreadContext>();

    var commitObject = new TestBase();
    var projectId = "projectId";
    var modelId = "modelId";
    var url = new Uri("https://localhost");
    var token = "token";
    var sourceApplication = "sourceApplication";
    var account = new Account()
    {
      userInfo = new UserInfo() { email = "test_user@example.com" },
      serverInfo = new ServerInfo() { url = url.ToString() },
      token = token
    };
    var sendInfo = new SendInfo(account, projectId, modelId, sourceApplication);
    var progress = Create<IProgress<CardProgress>>(MockBehavior.Loose);

    var ct = new CancellationToken();

    var rootId = "rootId";
    var refs = new Dictionary<Id, ObjectReference>();
    var serializeProcessResults = new SerializeProcessResults(rootId, refs);
    activityFactory.Setup(x => x.Start("SendOperation", "Send")).Returns((ISdkActivity?)null);

    sendOperationExecutor
      .Setup(x => x.Send(url, projectId, token, commitObject, It.IsAny<PassthroughProgress>(), ct))
      .ReturnsAsync(serializeProcessResults);

    sendConversionCache.Setup(x => x.StoreSendResult(projectId, refs));
    sendProgress.Setup(x => x.Begin());

    var sp = services.BuildServiceProvider();

    var sendOperation = ActivatorUtilities.CreateInstance<SendOperation<object>>(
      sp,
      rootObjectBuilder.Object,
      sendConversionCache.Object,
      sendProgress.Object,
      sendOperationExecutor.Object,
      activityFactory.Object,
      threadContext.Object
    );
    var result = await sendOperation.SendObjects(commitObject, projectId, account, progress.Object, ct);
    result.Should().Be(serializeProcessResults);
  }
}
