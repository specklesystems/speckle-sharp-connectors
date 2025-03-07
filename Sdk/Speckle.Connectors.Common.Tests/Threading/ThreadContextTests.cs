using FluentAssertions;
using NUnit.Framework;
using Speckle.Testing;

namespace Speckle.Connectors.Common.Tests.Threading;

public class ThreadContextTests : MoqTest
{
  [Test]
  [TestCase(true, true, Funcs.RunMain)]
  [TestCase(true, false, Funcs.MainToWorker)]
  [TestCase(false, true, Funcs.WorkerToMain)]
  [TestCase(false, false, Funcs.RunWorker)]
  public async Task RunOnThread(bool isMain, bool useMain, Funcs? result)
  {
    var tc = new TestThreadContext(isMain);
    bool resultRan = false;
    await tc.RunOnThread(
      () =>
      {
        resultRan = true;
      },
      useMain
    );
    resultRan.Should().BeTrue();
    tc.Func.Should().Be(result);
  }

  [Test]
  [TestCase(true, true, Funcs.RunMainAsync_T)]
  [TestCase(true, false, Funcs.MainToWorker)]
  [TestCase(false, true, Funcs.WorkerToMain)]
  [TestCase(false, false, Funcs.RunWorkerAsync_T)]
  public async Task RunOnThread_T(bool isMain, bool useMain, Funcs? result)
  {
    var tc = new TestThreadContext(isMain);
    bool resultRan = false;
    var x = await tc.RunOnThread(
      () =>
      {
        resultRan = true;
        return false;
      },
      useMain
    );
    resultRan.Should().BeTrue();
    x.Should().BeFalse();
    tc.Func.Should().Be(result);
  }

  [Test]
  [TestCase(true, true, Funcs.RunMainAsync)]
  [TestCase(true, false, Funcs.MainToWorkerAsync)]
  [TestCase(false, true, Funcs.WorkerToMainAsync)]
  [TestCase(false, false, Funcs.RunWorkerAsync)]
  public async Task RunOnThreadAsync(bool isMain, bool useMain, Funcs? result)
  {
    var tc = new TestThreadContext(isMain);
    bool resultRan = false;
    await tc.RunOnThreadAsync(
      () =>
      {
        resultRan = true;
        return Task.CompletedTask;
      },
      useMain
    );
    resultRan.Should().BeTrue();
    tc.Func.Should().Be(result);
  }

  [Test]
  [TestCase(true, true, Funcs.RunMainAsync_T)]
  [TestCase(true, false, Funcs.MainToWorkerAsync)]
  [TestCase(false, true, Funcs.WorkerToMainAsync)]
  [TestCase(false, false, Funcs.RunWorkerAsync_T)]
  public async Task RunOnThreadAsync_T(bool isMain, bool useMain, Funcs? result)
  {
    var tc = new TestThreadContext(isMain);
    bool resultRan = false;
    var x = await tc.RunOnThreadAsync(
      () =>
      {
        resultRan = true;
        return Task.FromResult(false);
      },
      useMain
    );
    resultRan.Should().BeTrue();
    x.Should().BeFalse();
    tc.Func.Should().Be(result);
  }
}
