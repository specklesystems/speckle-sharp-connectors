using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;
using Speckle.Testing;

namespace Speckle.Connectors.DUI.Tests;

public class SendOperationManagerTests : MoqTest
{
  [Test]
#pragma warning disable CA1506
  public async Task TestHappyProcess()
#pragma warning restore CA1506
  {
    // Arrange
    var serviceScopeMock = Create<IServiceScope>();
    var serviceProviderMock = Create<IServiceProvider>();
    serviceScopeMock.Setup(x => x.ServiceProvider).Returns(serviceProviderMock.Object);
    serviceScopeMock.Setup(x => x.Dispose());

    var operationProgressManager = Create<IOperationProgressManager>();
    var progressHandler = Create<IProgress<CardProgress>>();
    var modelCard = new SenderModelCard
    {
      ModelCardId = "model1",
      AccountId = "acc",
      ServerUrl = "http://localhost",
      ProjectId = "proj",
      ModelId = "mod"
    };
    var bridge = Create<IBrowserBridge>();

    operationProgressManager
      .Setup(x =>
        x.CreateOperationProgressEventHandler(bridge.Object, modelCard.ModelCardId, It.IsAny<CancellationToken>())
      )
      .Returns(progressHandler.Object);

    var store = new TestDocumentModelStore(
      Create<ILogger<DocumentModelStore>>(MockBehavior.Loose).Object,
      Create<IJsonSerializer>(MockBehavior.Loose).Object
    );

    store.AddModel(modelCard);

    var cancellationManager = Create<ICancellationManager>();
    var cancellationItem = Create<ICancellationItem>();
    var accountService = Create<IAccountService>();
    accountService
      .Setup(x => x.GetAccountWithServerUrlFallback(modelCard.AccountId, new(modelCard.ServerUrl)))
      .Returns(new Account());
    cancellationItem.Setup(x => x.Token).Returns(CancellationToken.None);
    cancellationItem.Setup(x => x.Dispose());
    cancellationManager.Setup(x => x.GetCancellationItem(modelCard.ModelCardId)).Returns(cancellationItem.Object);

    var speckleApplication = Create<ISpeckleApplication>();
    speckleApplication.SetupGet(x => x.ApplicationAndVersion).Returns("TestApp 1.0");

    var activityFactory = Create<ISdkActivityFactory>();
    var activity = Create<ISdkActivity>();
    activityFactory.Setup(x => x.Start(null, It.IsAny<string>())).Returns(activity.Object);
    activity.Setup(x => x.Dispose());

    var logger = Create<ILogger<SendOperationManager>>(MockBehavior.Loose);

    var sendResults = new List<SendConversionResult>();
    var versionId = "v1";
    var objects = new List<string> { "obj1", "obj2" };

    var sendOperationMock = Create<ISendOperation<string>>();
    sendOperationMock
      .Setup(x => x.Execute(objects, It.IsAny<SendInfo>(), progressHandler.Object, It.IsAny<CancellationToken>()))
      .ReturnsAsync(
        new SendOperationResult("rootObjId", versionId, new Dictionary<Id, ObjectReference>(), sendResults)
      );

    serviceProviderMock.Setup(x => x.GetService(typeof(ISendOperation<string>))).Returns(sendOperationMock.Object);

    var commandsMock = Create<ISendBindingUICommands>();
    commandsMock.Setup(x => x.Bridge).Returns(bridge.Object);
    commandsMock
      .Setup(x => x.SetModelSendResult(modelCard.ModelCardId, versionId, sendResults))
      .Returns(Task.CompletedTask);

    using var manager = new SendOperationManager(
      serviceScopeMock.Object,
      operationProgressManager.Object,
      store,
      cancellationManager.Object,
      speckleApplication.Object,
      activityFactory.Object,
      accountService.Object,
      logger.Object
    );

    // Act
    await manager.Process(commandsMock.Object, "model1", (sp, card) => { }, card => objects);
  }

  // Helper for in-memory DocumentModelStore
  private sealed class TestDocumentModelStore : DocumentModelStore
  {
    public TestDocumentModelStore(ILogger<DocumentModelStore> logger, IJsonSerializer serializer)
      : base(logger, serializer) { }

    protected override void HostAppSaveState(string modelCardState) { }

    protected override void LoadState() => throw new NotImplementedException();
  }
}
