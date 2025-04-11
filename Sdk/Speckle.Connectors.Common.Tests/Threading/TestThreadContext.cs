using FluentAssertions;
using Speckle.Connectors.Common.Threading;

namespace Speckle.Connectors.Common.Tests.Threading;

public class TestThreadContext(bool isMain) : ThreadContext
{
  public override bool IsMainThread => isMain;

  public Funcs? Func { get; set; }

  protected override Task RunMain(Action action)
  {
    action();
    Func.Should().BeNull();
    Func = Funcs.RunMain;
    return Task.CompletedTask;
  }

  protected override Task RunWorker(Action action)
  {
    action();
    Func.Should().BeNull();
    Func = Funcs.RunWorker;
    return Task.CompletedTask;
  }

  protected override async Task<T> WorkerToMainAsync<T>(Func<Task<T>> action)
  {
    var x = await action();
    Func.Should().BeNull();
    Func = Funcs.WorkerToMainAsync;
    return x;
  }

  protected override async Task<T> MainToWorkerAsync<T>(Func<Task<T>> action)
  {
    var x = await action();
    Func.Should().BeNull();
    Func = Funcs.MainToWorkerAsync;
    return x;
  }

  protected override Task<T> WorkerToMain<T>(Func<T> action)
  {
    var x = action();
    Func.Should().BeNull();
    Func = Funcs.WorkerToMain;
    return Task.FromResult(x);
  }

  protected override Task<T> MainToWorker<T>(Func<T> action)
  {
    var x = action();
    Func.Should().BeNull();
    Func = Funcs.MainToWorker;
    return Task.FromResult(x);
  }

  protected override Task<T> RunMainAsync<T>(Func<T> action)
  {
    var x = action();
    Func.Should().BeNull();
    Func = Funcs.RunMainAsync_T;
    return Task.FromResult(x);
  }

  protected override Task<T> RunWorkerAsync<T>(Func<T> action)
  {
    var x = action();
    Func.Should().BeNull();
    Func = Funcs.RunWorkerAsync_T;
    return Task.FromResult(x);
  }

  protected override async Task RunMainAsync(Func<Task> action)
  {
    await action();
    Func.Should().BeNull();
    Func = Funcs.RunMainAsync;
  }

  protected override async Task RunWorkerAsync(Func<Task> action)
  {
    await action();
    Func.Should().BeNull();
    Func = Funcs.RunWorkerAsync;
  }

  protected override async Task<T> RunMainAsync<T>(Func<Task<T>> action)
  {
    var x = await action();
    Func.Should().BeNull();
    Func = Funcs.RunMainAsync_T;
    return x;
  }

  protected override async Task<T> RunWorkerAsync<T>(Func<Task<T>> action)
  {
    var x = await action();
    Func.Should().BeNull();
    Func = Funcs.RunWorkerAsync_T;
    return x;
  }
}
