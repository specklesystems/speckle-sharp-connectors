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
  public void SubscribeInternalTest()
  {
    var handler = Create<ITopLevelExceptionHandler>();
    var sut = new IdleCallManager(handler.Object);
    var action = Create<Action>();
    var addEvent = Create<Action>();
    addEvent.Setup(x => x.Invoke());

    sut.IdleSubscriptionCalled.Should().BeFalse();
    sut.SubscribeInternal("id", action.Object, addEvent.Object);
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
    handler.Setup(x => x.CatchUnhandled(It.IsAny<Action>())).Returns(new Result());
    sut.AppOnIdle(removeEvent.Object);
  }

  [Test]
  public void AppOnIdleInternalTest()
  {
    var handler = Create<ITopLevelExceptionHandler>();
    var sut = new IdleCallManager(handler.Object);
    Action expectedAction = () => { };
    handler.Setup(x => x.CatchUnhandled(expectedAction)).Returns(new Result());

    var removeEvent = Create<Action>();
    removeEvent.Setup(x => x.Invoke());

    sut.SubscribeInternal("Test", expectedAction, () => { });
    sut.IdleSubscriptionCalled.Should().BeTrue();
    sut.Calls.Count.Should().Be(1);

    sut.AppOnIdleInternal(removeEvent.Object);
    sut.Calls.Count.Should().Be(0);
    sut.IdleSubscriptionCalled.Should().BeFalse();
  }
}
