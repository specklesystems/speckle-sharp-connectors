using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;
using Speckle.Testing;

namespace Speckle.Connectors.DUI.Tests;

[TestFixture]
public class ReceiveOperationManagerTests : MoqTest
{
  private Mock<IServiceScope> _serviceScopeMock;
  private Mock<ICancellationManager> _cancellationManagerMock;
  private Mock<IDocumentModelStore> _storeMock;
  private Mock<ISpeckleApplication> _speckleAppMock;
  private Mock<IOperationProgressManager> _progressManagerMock;
  private Mock<ILogger<ReceiveOperationManager>> _loggerMock;
  private ReceiveOperationManager _manager;

  [SetUp]
  public void SetUp()
  {
    _serviceScopeMock = Create<IServiceScope>();
    _cancellationManagerMock = Create<ICancellationManager>();
    _storeMock = Create<IDocumentModelStore>();
    _speckleAppMock = Create<ISpeckleApplication>();
    _progressManagerMock = Create<IOperationProgressManager>();
    _loggerMock = Create<ILogger<ReceiveOperationManager>>(MockBehavior.Loose);
    _manager = new ReceiveOperationManager(
      _serviceScopeMock.Object,
      _cancellationManagerMock.Object,
      _storeMock.Object,
      _speckleAppMock.Object,
      _progressManagerMock.Object,
      _loggerMock.Object
    );
    _serviceScopeMock.Setup(x => x.Dispose());
  }

  [TearDown]
  public void TearDown()
  {
    _manager.Dispose();
  }

  [Test]
  public void Process_ShouldThrow_WhenModelCardNotFound()
  {
    _storeMock.Setup(x => x.GetModelById("id1")).Returns((ModelCard?)null);
    var commands = Create<IReceiveBindingUICommands>();
    Assert.ThrowsAsync<InvalidOperationException>(
      async () =>
        await _manager.Process(
          commands.Object,
          "id1",
          (_, _) => { },
          (s, f) => Task.FromResult<HostObjectBuilderResult?>(null)
        )
    );
  }

  [Test]
  public async Task Process_ShouldHandleOperationCanceledException()
  {
    var modelCard = new ReceiverModelCard { ModelCardId = "id2", ModelName = "Test" };
    _storeMock.Setup(x => x.GetModelById("id2")).Returns(modelCard);
    var cancellationItem = Create<ICancellationItem>();
    cancellationItem.Setup(x => x.Token).Returns(CancellationToken.None);
    cancellationItem.Setup(x => x.Dispose());

    _cancellationManagerMock.Setup(x => x.GetCancellationItem("id2")).Returns(cancellationItem.Object);
    _cancellationManagerMock.Setup(x => x.CancelOperation("id2"));

    var receiveOp = Create<IReceiveOperation>();
    var serviceProvider = Create<IServiceProvider>();
    serviceProvider.Setup(x => x.GetService(typeof(IReceiveOperation))).Returns(receiveOp.Object);
    _serviceScopeMock.Setup(x => x.ServiceProvider).Returns(serviceProvider.Object);

    var exception = new OperationCanceledException();

    var commands = Create<IReceiveBindingUICommands>();
    commands.Setup(x => x.Bridge).Returns(Create<IBrowserBridge>().Object);

    var progressHandler = Create<IProgress<CardProgress>>();
    _progressManagerMock
      .Setup(x =>
        x.CreateOperationProgressEventHandler(It.IsAny<IBrowserBridge>(), "id2", It.IsAny<CancellationToken>())
      )
      .Returns(progressHandler.Object);

    var processor = new Func<string?, Func<Task<HostObjectBuilderResult>>, Task<HostObjectBuilderResult?>>(
      (s, f) => throw exception
    );
    await _manager.Process(commands.Object, "id2", (_, _) => { }, processor);
    _cancellationManagerMock.Verify(x => x.CancelOperation("id2"), Times.Once);
  }

  [Test]
  public async Task Process_ShouldHandleGeneralException_AndSetModelError()
  {
    var modelCard = new ReceiverModelCard { ModelCardId = "id3", ModelName = "Test" };
    _storeMock.Setup(x => x.GetModelById("id3")).Returns(modelCard);

    var cancellationItem = Create<ICancellationItem>();
    cancellationItem.Setup(x => x.Token).Returns(CancellationToken.None);
    cancellationItem.Setup(x => x.Dispose());

    _cancellationManagerMock.Setup(x => x.GetCancellationItem("id3")).Returns(cancellationItem.Object);
    _cancellationManagerMock.Setup(x => x.CancelOperation("id3"));

    var commands = Create<IReceiveBindingUICommands>();
    var exception = new InvalidOperationException("fail");
    commands.Setup(x => x.SetModelError("id3", exception)).Returns(Task.CompletedTask);
    var bridge = Create<IBrowserBridge>();
    commands.Setup(x => x.Bridge).Returns(bridge.Object);

    var progressHandler = Create<IProgress<CardProgress>>();
    _progressManagerMock
      .Setup(x => x.CreateOperationProgressEventHandler(bridge.Object, "id3", It.IsAny<CancellationToken>()))
      .Returns(progressHandler.Object);

    var receiveOp = Create<IReceiveOperation>();
    var serviceProvider = Create<IServiceProvider>();
    serviceProvider.Setup(x => x.GetService(typeof(IReceiveOperation))).Returns(receiveOp.Object);
    _serviceScopeMock.Setup(x => x.ServiceProvider).Returns(serviceProvider.Object);

    var processor = new Func<string?, Func<Task<HostObjectBuilderResult>>, Task<HostObjectBuilderResult?>>(
      (s, f) => throw exception
    );
    await _manager.Process(commands.Object, "id3", (_, _) => { }, processor);

    commands.Verify(x => x.SetModelError("id3", It.IsAny<Exception>()), Times.Once);
    _cancellationManagerMock.Verify(x => x.CancelOperation("id3"), Times.Once);
  }

  [Test]
  public async Task Process_ShouldSetModelReceiveResult_OnSuccess()
  {
    var modelCard = new ReceiverModelCard
    {
      ModelCardId = "id4",
      ModelName = "Test",
      AccountId = "AccountId",
      ServerUrl = "http://localhost",
      ProjectId = "ProjectId",
      ProjectName = "ProjectName",
      ModelId = "ModelId",
      SelectedVersionId = "SelectedVersionId",
    };
    _storeMock.Setup(x => x.GetModelById("id4")).Returns(modelCard);
    var cancellationItem = Create<ICancellationItem>();
    cancellationItem.Setup(x => x.Token).Returns(CancellationToken.None);
    cancellationItem.Setup(x => x.Dispose());

    _cancellationManagerMock.Setup(x => x.CancelOperation("id4"));
    _cancellationManagerMock.Setup(x => x.GetCancellationItem("id4")).Returns(cancellationItem.Object);

    var commands = Create<IReceiveBindingUICommands>();
    commands.Setup(x => x.Bridge).Returns(Create<IBrowserBridge>().Object);
    commands
      .Setup(x =>
        x.SetModelReceiveResult("id4", It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<ConversionResult>>())
      )
      .Returns(Task.CompletedTask);

    var progressHandler = Create<IProgress<CardProgress>>();
    _progressManagerMock
      .Setup(x =>
        x.CreateOperationProgressEventHandler(It.IsAny<IBrowserBridge>(), "id4", It.IsAny<CancellationToken>())
      )
      .Returns(progressHandler.Object);

    var receiveOp = Create<IReceiveOperation>();
    var bakedIds = new List<string> { "obj1", "obj2" };
    var hostResult = new HostObjectBuilderResult(bakedIds, []);
    var serviceProvider = Create<IServiceProvider>();
    serviceProvider.Setup(x => x.GetService(typeof(IReceiveOperation))).Returns(receiveOp.Object);
    _serviceScopeMock.Setup(x => x.ServiceProvider).Returns(serviceProvider.Object);
    receiveOp
      .Setup(x => x.Execute(It.IsAny<ReceiveInfo>(), progressHandler.Object, CancellationToken.None))
      .ReturnsAsync(hostResult);
    _speckleAppMock.Setup(x => x.Slug).Returns("slug");

    var processor = new Func<string?, Func<Task<HostObjectBuilderResult>>, Task<HostObjectBuilderResult?>>(
      async (s, f) => await f()
    );
    await _manager.Process(commands.Object, "id4", (_, _) => { }, processor);

    commands.Verify(
      x => x.SetModelReceiveResult("id4", bakedIds, It.IsAny<IEnumerable<ConversionResult>>()),
      Times.Once
    );
    _cancellationManagerMock.Verify(x => x.CancelOperation("id4"), Times.Once);
  }
}
