using FluentAssertions;
using Moq;
using NUnit.Framework;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Common.Tests.Operations;

public class ReceiveConversionHandlerTests
{
  [Test]
  public void TryConvert_ReturnsNullOnSuccess()
  {
    var activityFactory = new Mock<ISdkActivityFactory>();
    var activity = new Mock<ISdkActivity>();
    activityFactory.Setup(f => f.Start(It.IsAny<string>(), It.IsAny<string>())).Returns(activity.Object);
    var handler = new ReceiveConversionHandler(activityFactory.Object);

    Exception? result = handler.TryConvert(
      () => { /* success */
      }
    );

    result.Should().BeNull();
    activity.Verify(a => a.SetStatus(SdkActivityStatusCode.Ok), Times.Once);
  }

  [Test]
  public void TryConvert_ReturnsConversionException()
  {
    var activityFactory = new Mock<ISdkActivityFactory>();
    var activity = new Mock<ISdkActivity>();
    activityFactory.Setup(f => f.Start(It.IsAny<string>(), It.IsAny<string>())).Returns(activity.Object);
    var handler = new ReceiveConversionHandler(activityFactory.Object);
    var ex = new ConversionException("fail");

    Exception? result = handler.TryConvert(() => throw ex);

    result.Should().Be(ex);
    activity.Verify(a => a.SetStatus(SdkActivityStatusCode.Error), Times.Once);
  }

  [Test]
  public void TryConvert_ThrowsOperationCanceledException()
  {
    var activityFactory = new Mock<ISdkActivityFactory>();
    var activity = new Mock<ISdkActivity>();
    activityFactory.Setup(f => f.Start(It.IsAny<string>(), It.IsAny<string>())).Returns(activity.Object);
    var handler = new ReceiveConversionHandler(activityFactory.Object);

    Assert.Throws<OperationCanceledException>(() => handler.TryConvert(() => throw new OperationCanceledException()));
    activity.Verify(a => a.SetStatus(SdkActivityStatusCode.Error), Times.Once);
  }

  [Test]
  public void TryConvert_ReturnsNonFatalException()
  {
    var activityFactory = new Mock<ISdkActivityFactory>();
    var activity = new Mock<ISdkActivity>();
    activityFactory.Setup(f => f.Start(It.IsAny<string>(), It.IsAny<string>())).Returns(activity.Object);
    var handler = new ReceiveConversionHandler(activityFactory.Object);
#pragma warning disable CA2201
    var ex = new Exception("non-fatal");
#pragma warning restore CA2201

    Exception? result = handler.TryConvert(() => throw ex);

    result.Should().Be(ex);
    activity.Verify(a => a.SetStatus(SdkActivityStatusCode.Error), Times.Once);
    activity.Verify(a => a.RecordException(ex), Times.Once);
  }
}
