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
public class TestOneTimeEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : OneTimeThreadedEvent<object>(threadContext, exceptionHandler);

public class EventAggregatorTests: MoqTest
{
  [Test]
  public async Task Sub_Async_Arg_DisposeToken()
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
  public async Task Sub_Async_Arg_SubscribeToken()
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
  public async Task Sub_Async_NoArg()
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
  
  
  [Test]
  public async Task Sub_Sync_NoArg()
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
  
  
  [Test]
  public async Task Sub_Sync_Arg()
  {
    var services = new ServiceCollection();
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton(Create<ITopLevelExceptionHandler>().Object);
    services.AddTransient<TestEvent>();

    var val = false;
    var eventAggregator = new EventAggregator(services.BuildServiceProvider());
    var subscriptionToken =   eventAggregator.GetEvent<TestEvent>().Subscribe(x =>
    {
      val = true;
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
  
  [Test]
  public async Task Onetime__Async_Arg()
  {
    var services = new ServiceCollection();
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton(Create<ITopLevelExceptionHandler>().Object);
    services.AddTransient<TestOneTimeEvent>();

    var val = false;
    var eventAggregator = new EventAggregator(services.BuildServiceProvider());
    var subscriptionToken = eventAggregator.GetEvent<TestOneTimeEvent>().OneTimeSubscribe("test", _ =>
    {
      val = true;
      return Task.CompletedTask;
    });
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();

    await eventAggregator.GetEvent<TestOneTimeEvent>().PublishAsync(new object());

    val.Should().BeTrue();
    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
    subscriptionToken.Dispose();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }
  [Test]
  public async Task Onetime_Async_NoArg()
  {
    var services = new ServiceCollection();
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton(Create<ITopLevelExceptionHandler>().Object);
    services.AddTransient<TestOneTimeEvent>();

    var val = false;
    var eventAggregator = new EventAggregator(services.BuildServiceProvider());
    var subscriptionToken = eventAggregator.GetEvent<TestOneTimeEvent>().OneTimeSubscribe("test", () =>
    {
      val = true;
      return Task.CompletedTask;
    });
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();

    await eventAggregator.GetEvent<TestOneTimeEvent>().PublishAsync(new object());

    val.Should().BeTrue();
    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
    subscriptionToken.Dispose();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }
  
  
  
  [Test]
  public async Task  Onetime_Sync_NoArg()
  {
    var services = new ServiceCollection();
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton(Create<ITopLevelExceptionHandler>().Object);
    services.AddTransient<TestOneTimeEvent>();

    var val = false;
    var eventAggregator = new EventAggregator(services.BuildServiceProvider());
    var subscriptionToken =   eventAggregator.GetEvent<TestOneTimeEvent>().OneTimeSubscribe("test",() =>
    {
      val = true;
    });
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();

    await eventAggregator.GetEvent<TestOneTimeEvent>().PublishAsync(new object());

    
    val.Should().BeTrue();
    eventAggregator.GetEvent<TestEvent>().Unsubscribe(subscriptionToken);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }
  
  
  [Test]
  public async Task Onetime_Sync_Arg()
  {
    var services = new ServiceCollection();
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton(Create<ITopLevelExceptionHandler>().Object);
    services.AddTransient<TestOneTimeEvent>();

    var val = false;
    var eventAggregator = new EventAggregator(services.BuildServiceProvider());
    var subscriptionToken =   eventAggregator.GetEvent<TestOneTimeEvent>().OneTimeSubscribe("test",x =>
    {
      val = true;
    });
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();

    await eventAggregator.GetEvent<TestOneTimeEvent>().PublishAsync(new object());

    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
    val.Should().BeTrue();
    eventAggregator.GetEvent<TestEvent>().Unsubscribe(subscriptionToken);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

}
