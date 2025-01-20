using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Testing;

namespace Speckle.Connectors.DUI.Tests.Eventing;

public class TestEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : ThreadedEvent<object>(threadContext, exceptionHandler);

public class EventAggregatorTests: MoqTest
{
  [Test]
  public async Task Sub_Task_Arg_DisposeToken()
  {
    var services = new ServiceCollection();
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton(Create<ITopLevelExceptionHandler>().Object);
    services.AddTransient<TestEvent>();

    var val = false;
    var eventAggregator = new EventAggregator(services.BuildServiceProvider());
    var subscriptionToken = eventAggregator.GetEvent<TestEvent>().Subscribe( _ =>
    {
       val = true;
       return Task.CompletedTask;
    });

    await eventAggregator.GetEvent<TestEvent>().PublishAsync(new object());

    val.Should().BeTrue();
    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();
    subscriptionToken.Dispose();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }
  
  [Test]
  public async Task Sub_Task_Arg_SubscribeToken()
  {
    var services = new ServiceCollection();
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton(Create<ITopLevelExceptionHandler>().Object);
    services.AddTransient<TestEvent>();

    var val = false;
    var eventAggregator = new EventAggregator(services.BuildServiceProvider());
    var subscriptionToken = eventAggregator.GetEvent<TestEvent>().Subscribe( _ =>
    {
      val = true;
      return Task.CompletedTask;
    });

    await eventAggregator.GetEvent<TestEvent>().PublishAsync(new object());

    val.Should().BeTrue();
    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();
    eventAggregator.GetEvent<TestEvent>().Unsubscribe(subscriptionToken);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }
  
  [Test]
  public async Task Sub_Task_NoArg()
  {
    var services = new ServiceCollection();
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton(Create<ITopLevelExceptionHandler>().Object);
    services.AddTransient<TestEvent>();

    var val = false;
    var eventAggregator = new EventAggregator(services.BuildServiceProvider());
    var subscriptionToken =   eventAggregator.GetEvent<TestEvent>().Subscribe(() =>
    {
      val = true;
      return Task.CompletedTask;
    });

    await eventAggregator.GetEvent<TestEvent>().PublishAsync(new object());

    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();
    val.Should().BeTrue();
    eventAggregator.GetEvent<TestEvent>().Unsubscribe(subscriptionToken);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }
}
