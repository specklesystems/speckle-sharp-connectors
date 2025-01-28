using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Testing;
using Xunit;

namespace Speckle.Connectors.DUI.Tests.Bridge;

public class TopLevelExceptionHandlerTests : MoqTest
{
  [Fact]
  public void CatchUnhandledAction_Happy()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var eventAggregator = Create<IEventAggregator>();
    var sut = new TopLevelExceptionHandler(logger.Object, eventAggregator.Object);

    sut.CatchUnhandled(() => { });
  }

  [Fact]
  public void CatchUnhandledAction_Exception()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var eventAggregator = Create<IEventAggregator>();

    eventAggregator
      .Setup(x => x.GetEvent<ExceptionEvent>())
      .Returns(new ExceptionEvent(Create<IThreadContext>().Object, Create<ITopLevelExceptionHandler>().Object));

    var sut = new TopLevelExceptionHandler(logger.Object, eventAggregator.Object);

    sut.CatchUnhandled(() => throw new InvalidOperationException());
  }

  [Fact]
  public void CatchUnhandledFunc_Happy()
  {
    var val = 2;
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var eventAggregator = Create<IEventAggregator>();
    var sut = new TopLevelExceptionHandler(logger.Object, eventAggregator.Object);

    var returnVal = sut.CatchUnhandled(() => val);
    returnVal.Value.Should().Be(val);
    returnVal.Exception.Should().BeNull();
    returnVal.IsSuccess.Should().BeTrue();
  }

  [Fact]
  public void CatchUnhandledFunc_Exception()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var eventAggregator = Create<IEventAggregator>();

    eventAggregator
      .Setup(x => x.GetEvent<ExceptionEvent>())
      .Returns(new ExceptionEvent(Create<IThreadContext>().Object, Create<ITopLevelExceptionHandler>().Object));

    var sut = new TopLevelExceptionHandler(logger.Object, eventAggregator.Object);

    var returnVal = sut.CatchUnhandled((Func<string>)(() => throw new InvalidOperationException()));
    returnVal.Value.Should().BeNull();
    returnVal.Exception.Should().BeOfType<InvalidOperationException>();
    returnVal.IsSuccess.Should().BeFalse();
  }

  [Fact]
  public void CatchUnhandledFunc_Exception_Fatal()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var eventAggregator = Create<IEventAggregator>();
    var sut = new TopLevelExceptionHandler(logger.Object, eventAggregator.Object);

    Assert.Throws<AppDomainUnloadedException>(
      () => sut.CatchUnhandled(new Func<string>(() => throw new AppDomainUnloadedException()))
    );
  }

  [Fact]
  public async Task CatchUnhandledFuncAsync_Happy()
  {
    var val = 2;
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var eventAggregator = Create<IEventAggregator>();
    var sut = new TopLevelExceptionHandler(logger.Object, eventAggregator.Object);

    var returnVal = await sut.CatchUnhandledAsync(() => Task.FromResult(val));
    returnVal.Value.Should().Be(val);
    returnVal.Exception.Should().BeNull();
    returnVal.IsSuccess.Should().BeTrue();
  }

  [Fact]
  public async Task CatchUnhandledFuncAsync_Exception()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var eventAggregator = Create<IEventAggregator>();

    eventAggregator
      .Setup(x => x.GetEvent<ExceptionEvent>())
      .Returns(new ExceptionEvent(Create<IThreadContext>().Object, Create<ITopLevelExceptionHandler>().Object));

    var sut = new TopLevelExceptionHandler(logger.Object, eventAggregator.Object);

    var returnVal = await sut.CatchUnhandledAsync(new Func<Task<string>>(() => throw new InvalidOperationException()));
    returnVal.Value.Should().BeNull();
    returnVal.Exception.Should().BeOfType<InvalidOperationException>();
    returnVal.IsSuccess.Should().BeFalse();
  }

  [Fact]
  public void CatchUnhandledFuncAsync_Exception_Fatal()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var eventAggregator = Create<IEventAggregator>();
    var sut = new TopLevelExceptionHandler(logger.Object, eventAggregator.Object);

    var exception = Assert.ThrowsAsync<AppDomainUnloadedException>(
      async () => await sut.CatchUnhandledAsync(new Func<Task<string>>(() => throw new AppDomainUnloadedException()))
    );
    exception.Should().BeOfType<AppDomainUnloadedException>();
  }
}
