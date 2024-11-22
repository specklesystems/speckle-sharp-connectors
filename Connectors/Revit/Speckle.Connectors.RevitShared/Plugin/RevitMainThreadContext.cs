﻿#nullable enable
namespace Speckle.Connectors.Revit.Plugin;

public class RevitMainThreadContext : MainThreadContext
{
  public override void RunContext(Action action) => 
    RevitTask.RunAsync(action);

  public override Task<T> RunContext<T>(Func<Task<T>> action) =>
    RevitTask.RunAsync(action);
}
