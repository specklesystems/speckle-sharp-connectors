using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Connectors.Common.Tests.Operations;

public class ReceiveConversionHandlerTests
{
  [Test]
  public void TryConvert_ReturnsNullOnSuccess()
  {
    var handler = new ReceiveConversionHandler();

    Exception? result = handler.TryConvert(
      () => { /* success */
      }
    );

    result.Should().BeNull();
  }

  [Test]
  public void TryConvert_ReturnsConversionException()
  {
    var handler = new ReceiveConversionHandler();
    var ex = new ConversionException("fail");

    Exception? result = handler.TryConvert(() => throw ex);

    result.Should().Be(ex);
  }

  [Test]
  public void TryConvert_ThrowsOperationCanceledException()
  {
    var handler = new ReceiveConversionHandler();

    Assert.Throws<OperationCanceledException>(() => handler.TryConvert(() => throw new OperationCanceledException()));
  }

  [Test]
  public void TryConvert_ReturnsNonFatalException()
  {
    var handler = new ReceiveConversionHandler();
#pragma warning disable CA2201
    var ex = new Exception("non-fatal");
#pragma warning restore CA2201

    Exception? result = handler.TryConvert(() => throw ex);

    result.Should().Be(ex);
  }
}
