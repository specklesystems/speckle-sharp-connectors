using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

public class TestPeriodicThreadedEvent(IThreadContext threadContext, ITopLevelExceptionHandler exceptionHandler)
  : PeriodicThreadedEvent(threadContext, exceptionHandler);

public class EventAggregatorTests : MoqTest
{
  [Test]
  public async Task Sub_Async_DisposeToken()
  {
    s_val = false;
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestEvent>();
    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();

    var subscriptionToken = Test_Sub_Async_DisposeToken(serviceProvider);
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    await eventAggregator.GetEvent<TestEvent>().PublishAsync(new object());

    s_val.Should().BeTrue();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();
    subscriptionToken.Dispose();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

  private SubscriptionToken Test_Sub_Async_DisposeToken(IServiceProvider serviceProvider)
  {
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    var subscriptionToken = eventAggregator.GetEvent<TestEvent>().Subscribe(OnTestAsyncSubscribe);
    return subscriptionToken;
  }

  [Test]
  public async Task Sub_Async_SubscribeToken()
  {
    s_val = false;
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestEvent>();
    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();

    var subscriptionToken = Test_Sub_Async_DisposeToken(serviceProvider);
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    await eventAggregator.GetEvent<TestEvent>().PublishAsync(new object());

    s_val.Should().BeTrue();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();
    eventAggregator.GetEvent<TestEvent>().Unsubscribe(subscriptionToken);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

  [Test]
  public async Task Sub_Sync()
  {
    s_val = false;
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestEvent>();

    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();

    var subscriptionToken = Test_Sub_Sync(serviceProvider);
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    await eventAggregator.GetEvent<TestEvent>().PublishAsync(new object());

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();
    s_val.Should().BeTrue();
    eventAggregator.GetEvent<TestEvent>().Unsubscribe(subscriptionToken);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

  private SubscriptionToken Test_Sub_Sync(IServiceProvider serviceProvider)
  {
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    var subscriptionToken = eventAggregator.GetEvent<TestEvent>().Subscribe(OnTestSyncSubscribe);
    return subscriptionToken;
  }

  [Test]
  public async Task Sub_Sync_Static()
  {
    s_val = false;
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestEvent>();

    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();

    var subscriptionToken = Test_Sub_Sync_Static(serviceProvider);
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    await eventAggregator.GetEvent<TestEvent>().PublishAsync(new object());

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();
    s_val.Should().BeTrue();
    eventAggregator.GetEvent<TestEvent>().Unsubscribe(subscriptionToken);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

  private static SubscriptionToken Test_Sub_Sync_Static(IServiceProvider serviceProvider)
  {
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    var subscriptionToken = eventAggregator.GetEvent<TestEvent>().Subscribe(OnTestSyncStaticSubscribe);
    return subscriptionToken;
  }

  private static void OnTestSyncStaticSubscribe(object _)
  {
    s_val = true;
  }

  [Test]
  public async Task Sub_Async_Static()
  {
    s_val = false;
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestEvent>();

    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();

    var subscriptionToken = Test_Sub_Async_Static(serviceProvider);
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    await eventAggregator.GetEvent<TestEvent>().PublishAsync(new object());

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();
    s_val.Should().BeTrue();
    eventAggregator.GetEvent<TestEvent>().Unsubscribe(subscriptionToken);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

  private static SubscriptionToken Test_Sub_Async_Static(IServiceProvider serviceProvider)
  {
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    var subscriptionToken = eventAggregator.GetEvent<TestEvent>().Subscribe(OnTestAsyncStaticSubscribe);
    return subscriptionToken;
  }

  private static Task OnTestAsyncStaticSubscribe(object _)
  {
    s_val = true;
    return Task.CompletedTask;
  }

  private Task OnTestAsyncSubscribe(object _)
  {
    s_val = true;
    return Task.CompletedTask;
  }

  [Test]
  public async Task Onetime_Async()
  {
    s_val = false;
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestOneTimeEvent>();

    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();

    var subscriptionToken = Test_Onetime_Sub_Async(serviceProvider);
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();

    await eventAggregator.GetEvent<TestOneTimeEvent>().PublishAsync(new object());

    s_val.Should().BeTrue();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
    subscriptionToken.Dispose();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

  private SubscriptionToken Test_Onetime_Sub_Async(IServiceProvider serviceProvider)
  {
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    var subscriptionToken = eventAggregator.GetEvent<TestOneTimeEvent>().OneTimeSubscribe("test", OnTestAsyncSubscribe);
    return subscriptionToken;
  }

  private SubscriptionToken Test_Onetime_Sub_Sync(IServiceProvider serviceProvider)
  {
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    var subscriptionToken = eventAggregator.GetEvent<TestOneTimeEvent>().OneTimeSubscribe("test", OnTestSyncSubscribe);
    return subscriptionToken;
  }

  private void OnTestSyncSubscribe(object _)
  {
    s_val = true;
  }

  [Test]
  public async Task Onetime_Sync()
  {
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestOneTimeEvent>();
    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();

    var subscriptionToken = Test_Onetime_Sub_Sync(serviceProvider);
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();

    await eventAggregator.GetEvent<TestOneTimeEvent>().PublishAsync(new object());

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
    s_val.Should().BeTrue();
    eventAggregator.GetEvent<TestOneTimeEvent>().Unsubscribe(subscriptionToken);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

  private static bool s_val;

  [Test]
  public async Task Sub_WeakReference()
  {
    s_val = false;
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestEvent>();
    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();
    TestWeakReference(serviceProvider);

    s_val.Should().BeFalse();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    await serviceProvider.GetRequiredService<IEventAggregator>().GetEvent<TestEvent>().PublishAsync(new object());
    s_val.Should().BeFalse();
  }

  //keep in a separate method to avoid referencing ObjectWithAction
  private static void TestWeakReference(IServiceProvider serviceProvider)
  {
    var x = new ObjectWithAction(
      new Action(() =>
      {
        s_val = true;
      })
    );
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    eventAggregator.GetEvent<TestEvent>().Subscribe(x.Test1);
    x = null;
  }

  private sealed class ObjectWithAction(Action action)
  {
    public void Test1(object _)
    {
      action();
    }
  }

  [Test]
  public async Task Periodic_Async()
  {
    s_val = false;
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestPeriodicThreadedEvent>();

    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();

    var subscriptionToken = Test_Periodic_Sub_Async(serviceProvider);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();

    await Task.Delay(2000);
    s_val.Should().BeTrue();
    subscriptionToken.Unsubscribe();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
    subscriptionToken.Dispose();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

  private SubscriptionToken Test_Periodic_Sub_Async(IServiceProvider serviceProvider)
  {
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    var subscriptionToken = eventAggregator
      .GetEvent<TestPeriodicThreadedEvent>()
      .SubscribePeriodic(TimeSpan.FromSeconds(1), OnTestAsyncSubscribe);
    return subscriptionToken;
  }

  [Test]
  public async Task Periodic_Sync()
  {
    s_val = false;
    var services = new ServiceCollection();
    var exceptionHandler = new TopLevelExceptionHandler(
      Create<ILogger<TopLevelExceptionHandler>>().Object,
      Create<IEventAggregator>().Object
    );
    services.AddSingleton(Create<IThreadContext>().Object);
    services.AddSingleton<ITopLevelExceptionHandler>(exceptionHandler);
    services.AddTransient<TestPeriodicThreadedEvent>();

    services.AddSingleton<IEventAggregator, EventAggregator>();
    var serviceProvider = services.BuildServiceProvider();

    var subscriptionToken = Test_Periodic_Sub_Sync(serviceProvider);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeTrue();

    await Task.Delay(2000);
    s_val.Should().BeTrue();
    subscriptionToken.Unsubscribe();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
    subscriptionToken.Dispose();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    subscriptionToken.IsActive.Should().BeFalse();
  }

  private SubscriptionToken Test_Periodic_Sub_Sync(IServiceProvider serviceProvider)
  {
    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
    var subscriptionToken = eventAggregator
      .GetEvent<TestPeriodicThreadedEvent>()
      .SubscribePeriodic(TimeSpan.FromSeconds(1), OnTestSyncSubscribe);
    return subscriptionToken;
  }
}
