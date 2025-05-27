using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Models;
using Speckle.Testing;
using Moq;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.DUI.Tests;

public class SendOperationManagerTests: MoqTest
{
  [Test]
  public async Task TestHappyProcess()
  {
    // Arrange
    var serviceScopeMock = Create<IServiceScope>();
    var serviceProviderMock =Create<IServiceProvider>();
    serviceScopeMock.Setup(x => x.ServiceProvider).Returns(serviceProviderMock.Object);

    var operationProgressManager = Create<IOperationProgressManager>();
    var progressHandler = Create<IProgress<CardProgress>>();
    
    operationProgressManager.Setup(x => x.CreateOperationProgressEventHandler(It.IsAny<IBrowserBridge>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns(progressHandler.Object);

    var store = new TestDocumentModelStore(Create<ILogger<DocumentModelStore>>(MockBehavior.Loose).Object, Create<IJsonSerializer>().Object);
    var modelCard = new SenderModelCard
    {
      ModelCardId = "model1",
      AccountId = "acc",
      ServerUrl = "http://localhost",
      ProjectId = "proj",
      ModelId = "mod"
    };
    store.AddModel(modelCard);

    var cancellationManager = Create<ICancellationManager>();
    var cancellationItem = Create<ICancellationItem>();
    cancellationItem.SetupGet(x => x.Token).Returns(CancellationToken.None);
    cancellationManager.Setup(x => x.GetCancellationItem(It.IsAny<string>())).Returns(cancellationItem.Object);

    var speckleApplication = Create<ISpeckleApplication>();
    speckleApplication.SetupGet(x => x.ApplicationAndVersion).Returns("TestApp 1.0");

    var activityFactory = Create<ISdkActivityFactory>();
    var activity = Create<IDisposable>();
    activityFactory.Setup(x => x.Start()).Returns(activity.Object);

    var logger = Create<ILogger<SendOperationManager>>();

    var sendOperationMock = Create<SendOperation<string>>();
    sendOperationMock.Setup(x => x.Execute(
      It.IsAny<IReadOnlyList<string>>(),
      It.IsAny<SendInfo>(),
      It.IsAny<IProgress<CardProgress>>(),
      It.IsAny<CancellationToken>()))
      .ReturnsAsync(new SendResult { VersionId = "v1", ConversionResults = new List<string> { "ok" } });

    serviceProviderMock.Setup(x => x.GetService(typeof(SendOperation<string>)))
      .Returns(sendOperationMock.Object);
    serviceProviderMock.Setup(x => x.GetRequiredService<SendOperation<string>>())
      .Returns(sendOperationMock.Object);

    var commandsMock = Create<SendBindingUICommands>();
    commandsMock.Setup(x => x.SetModelSendResult("model1", "v1", It.IsAny<IReadOnlyList<string>>()))
      .Returns(Task.CompletedTask)
      .Verifiable();

    var manager = new SendOperationManager(
      serviceScopeMock.Object,
      operationProgressManager.Object,
      store,
      cancellationManager.Object,
      speckleApplication.Object,
      activityFactory.Object,
      logger.Object
    );

    // Act
    await manager.Process(
      commandsMock.Object,
      "model1",
      (sp, card) => { },
      card => new List<string> { "obj1" }
    );

    // Assert
    commandsMock.Verify();
  }

  // Helper for in-memory DocumentModelStore
  private class TestDocumentModelStore : DocumentModelStore
  {
    public TestDocumentModelStore(ILogger<DocumentModelStore> logger, IJsonSerializer serializer) : base(
    logger, serializer)
    {
    }

    protected override void HostAppSaveState(string modelCardState) => throw new NotImplementedException();

    protected override void LoadState() => throw new NotImplementedException();
  }
}
