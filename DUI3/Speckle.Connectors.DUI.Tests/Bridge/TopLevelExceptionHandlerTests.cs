﻿using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Testing;

namespace Speckle.Connectors.DUI.Tests.Bridge;

public class TopLevelExceptionHandlerTests : MoqTest
{
  [Test]
  public void CatchUnhandledAction_Happy()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var bridge = Create<IBridge>();
    var sut = new TopLevelExceptionHandler(logger.Object, bridge.Object);

    sut.CatchUnhandled(() => { });
  }

  [Test]
  public void CatchUnhandledAction_Exception()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var bridge = Create<IBridge>();

    bridge.Setup(x => x.Send(BasicConnectorBindingCommands.SET_GLOBAL_NOTIFICATION, It.IsAny<object>()));

    var sut = new TopLevelExceptionHandler(logger.Object, bridge.Object);

    sut.CatchUnhandled(() => throw new InvalidOperationException());
  }

  [Test]
  public void CatchUnhandledFunc_Happy()
  {
    var val = 2;
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var bridge = Create<IBridge>();
    var sut = new TopLevelExceptionHandler(logger.Object, bridge.Object);

    var returnVal = sut.CatchUnhandled(() => val);
    returnVal.Value.Should().Be(val);
    returnVal.Exception.Should().BeNull();
    returnVal.IsSuccess.Should().BeTrue();
  }

  [Test]
  public void CatchUnhandledFunc_Exception()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var bridge = Create<IBridge>();

    bridge.Setup(x => x.Send(BasicConnectorBindingCommands.SET_GLOBAL_NOTIFICATION, It.IsAny<object>()));

    var sut = new TopLevelExceptionHandler(logger.Object, bridge.Object);

    var returnVal = sut.CatchUnhandled((Func<string>)(() => throw new InvalidOperationException()));
    returnVal.Value.Should().BeNull();
    returnVal.Exception.Should().BeOfType<InvalidOperationException>();
    returnVal.IsSuccess.Should().BeFalse();
  }

  [Test]
  public void CatchUnhandledFunc_Exception_Fatal()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var bridge = Create<IBridge>();
    var sut = new TopLevelExceptionHandler(logger.Object, bridge.Object);

#pragma warning disable CA2201
    var exception = Assert.Throws<AggregateException>(
      () => sut.CatchUnhandled(new Func<string>(() => throw new AppDomainUnloadedException()))
    );
#pragma warning restore CA2201
    exception.InnerExceptions.Single().Should().BeOfType<AppDomainUnloadedException>();
  }

  [Test]
  public async Task CatchUnhandledFuncAsync_Happy()
  {
    var val = 2;
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var bridge = Create<IBridge>();
    var sut = new TopLevelExceptionHandler(logger.Object, bridge.Object);

    var returnVal = await sut.CatchUnhandled(() => Task.FromResult(val));
    returnVal.Value.Should().Be(val);
    returnVal.Exception.Should().BeNull();
    returnVal.IsSuccess.Should().BeTrue();
  }

  [Test]
  public async Task CatchUnhandledFuncAsync_Exception()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var bridge = Create<IBridge>();

    bridge.Setup(x => x.Send(BasicConnectorBindingCommands.SET_GLOBAL_NOTIFICATION, It.IsAny<object>()));

    var sut = new TopLevelExceptionHandler(logger.Object, bridge.Object);

    var returnVal = await sut.CatchUnhandled(new Func<Task<string>>(() => throw new InvalidOperationException()));
    returnVal.Value.Should().BeNull();
    returnVal.Exception.Should().BeOfType<InvalidOperationException>();
    returnVal.IsSuccess.Should().BeFalse();
  }

  [Test]
  public void CatchUnhandledFuncAsync_Exception_Fatal()
  {
    var logger = Create<ILogger<TopLevelExceptionHandler>>(MockBehavior.Loose);
    var bridge = Create<IBridge>();
    var sut = new TopLevelExceptionHandler(logger.Object, bridge.Object);

#pragma warning disable CA2201
    var exception = Assert.ThrowsAsync<AppDomainUnloadedException>(
      async () => await sut.CatchUnhandled(new Func<Task<string>>(() => throw new AppDomainUnloadedException()))
    );
#pragma warning restore CA2201
    exception.Should().BeOfType<AppDomainUnloadedException>();
  }
}