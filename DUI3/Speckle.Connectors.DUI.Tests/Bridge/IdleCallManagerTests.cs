using FluentAssertions;
using Moq;
using NUnit.Framework;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Testing;

namespace Speckle.Connectors.DUI.Tests.Bridge;

public class IdleCallManagerTests : MoqTest
{
  [Test]
  public void SubscribeToIdleTest()
  {
    var handler = Create<ITopLevelExceptionHandler>();
    var sut = new IdleCallManager(handler.Object);
    var action = Create<Action>();
    var addEvent = Create<Action>();
    handler.Setup(x => x.CatchUnhandled(It.IsAny<Action>())).Returns(new Result());
    sut.SubscribeToIdle("id", action.Object, addEvent.Object);
  }

  [Test]
  public void SubscribeToIdleTest_2()
  {
    var handler = Create<ITopLevelExceptionHandler>();
    var sut = new IdleCallManager(handler.Object);
    var action = Create<Func<Task>>();
    var addEvent = Create<Action>();
    addEvent.Setup(x => x.Invoke());

    //add
    handler.Setup(m => m.CatchUnhandled(It.IsAny<Action>())).Callback<Action>(a => a.Invoke()).Returns(new Result());

    sut.IdleSubscriptionCalled.Should().BeFalse();
    sut.SubscribeToIdle("id", action.Object, addEvent.Object);
    sut.Calls.Count.Should().Be(1);
    sut.Calls.Should().ContainKey("id");
    sut.IdleSubscriptionCalled.Should().BeTrue();
  }

  [Test]
  public void AppOnIdleTest()
  {
    var handler = Create<ITopLevelExceptionHandler>();
    var sut = new IdleCallManager(handler.Object);
    var removeEvent = Create<Action>();
    handler.Setup(x => x.FireAndForget(It.IsAny<Func<Task>>()));
    sut.AppOnIdle(removeEvent.Object);
  }

  [Test]
  public async Task AppOnIdleInternalTest()
  {
    var handler = Create<ITopLevelExceptionHandler>();
    var sut = new IdleCallManager(handler.Object);
    var expectedAction = Create<Func<Task>>();
    expectedAction.Setup(x => x.Invoke()).Returns(Task.CompletedTask);

    //add
    handler.Setup(m => m.CatchUnhandled(It.IsAny<Action>())).Returns(new Result());
    //idle
    handler
      .Setup(m => m.CatchUnhandledAsync(It.IsAny<Func<Task>>()))
      .Callback<Func<Task>>(a => a.Invoke())
      .ReturnsAsync(new Result());

    var removeEvent = Create<Action>();
    removeEvent.Setup(x => x.Invoke());

    sut.SubscribeToIdle("Test", expectedAction.Object, () => { });
    sut.IdleSubscriptionCalled.Should().BeTrue();
    sut.Calls.Count.Should().Be(1);

    await sut.AppOnIdleInternal(removeEvent.Object);
    sut.Calls.Count.Should().Be(0);
    sut.IdleSubscriptionCalled.Should().BeFalse();
    expectedAction.Verify(a => a(), Times.Once);
    removeEvent.Verify(a => a(), Times.Once);
  }
}
