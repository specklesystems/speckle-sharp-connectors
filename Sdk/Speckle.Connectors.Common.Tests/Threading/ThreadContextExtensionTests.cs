using FluentAssertions;
using Moq;
using NUnit.Framework;
using Speckle.Connectors.Common.Threading;
using Speckle.Testing;

namespace Speckle.Connectors.Common.Tests.Threading;

public class ThreadContextExtensionTests : MoqTest
{
  [Test]
  public async Task RunOnMain()
  {
    Action a = () => { };
    var tc = Create<IThreadContext>();
    tc.Setup(x => x.RunOnThread(a, true)).Returns(Task.CompletedTask);
    await tc.Object.RunOnMain(a);
  }

  [Test]
  public async Task RunOnWorker()
  {
    Action a = () => { };
    var tc = Create<IThreadContext>();
    tc.Setup(x => x.RunOnThread(a, false)).Returns(Task.CompletedTask);
    await tc.Object.RunOnWorker(a);
  }

  [Test]
  public async Task RunOnMain_T()
  {
    Func<bool> a = () => true;
    var tc = Create<IThreadContext>();
    tc.Setup(x => x.RunOnThread(a, true)).ReturnsAsync(true);
    (await tc.Object.RunOnMain(a)).Should().BeTrue();
  }

  [Test]
  public async Task RunOnWorker_T()
  {
    Func<bool> a = () => true;
    var tc = Create<IThreadContext>();
    tc.Setup(x => x.RunOnThread(a, false)).ReturnsAsync(true);
    (await tc.Object.RunOnWorker(a)).Should().BeTrue();
  }

  [Test]
  public async Task RunOnMainAsync()
  {
    Func<Task> a = () => Task.CompletedTask;
    var tc = Create<IThreadContext>();
    tc.Setup(x => x.RunOnThreadAsync(a, true)).Returns(Task.CompletedTask);
    await tc.Object.RunOnMainAsync(a);
  }

  [Test]
  public async Task RunOnWorkerAsync()
  {
    Func<Task> a = () => Task.CompletedTask;
    var tc = Create<IThreadContext>();
    tc.Setup(x => x.RunOnThreadAsync(a, false)).Returns(Task.CompletedTask);
    await tc.Object.RunOnWorkerAsync(a);
  }

  [Test]
  public async Task RunOnMainAsync_T()
  {
    Func<Task<bool>> a = () => Task.FromResult(true);
    var tc = Create<IThreadContext>();
    tc.Setup(x => x.RunOnThreadAsync(a, true)).ReturnsAsync(true);
    (await tc.Object.RunOnMainAsync(a)).Should().BeTrue();
  }

  [Test]
  public async Task RunOnWorkerAsync_T()
  {
    Func<Task<bool>> a = () => Task.FromResult(true);
    var tc = Create<IThreadContext>();
    tc.Setup(x => x.RunOnThreadAsync(a, false)).ReturnsAsync(true);
    await tc.Object.RunOnWorkerAsync(a);
    (await tc.Object.RunOnWorkerAsync(a)).Should().BeTrue();
  }

  [Test]
#pragma warning disable CA1030
  public async Task FireAndForget()
#pragma warning restore CA1030
  {
    //kind of does nothing, just making sure there's no error
    Task.CompletedTask.FireAndForget();
    await Task.Delay(500);
  }
}
