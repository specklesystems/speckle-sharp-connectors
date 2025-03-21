using Grasshopper.Kernel;
using Rhino;

namespace Speckle.Connectors.Grasshopper8.Components.BaseComponents;

public abstract class GH_AsyncComponent<TInput, TOutput> : SpeckleScopedTaskCapableComponent<TInput, TOutput>
{
  private readonly Action _done;

  private int _state;

  private int _setData;

  public List<WorkerInstance> Workers { get; set; } = new();

  private readonly List<Task> _tasks;

  public List<CancellationTokenSource> CancellationSources { get; set; }

  public override Guid ComponentGuid => Guid.Empty; // should be overriden in any descendant

  public WorkerInstance BaseWorker { get; set; }

  public TaskCreationOptions? TaskCreationOptions { get; set; }

  protected GH_AsyncComponent(string name, string nickname, string description, string category, string subCategory)
    : base(name, nickname, description, category, subCategory)
  {
    _done = delegate
    {
      Interlocked.Increment(ref _state);
      if (_state == Workers.Count && _setData == 0)
      {
        Interlocked.Exchange(ref _setData, 1);
        Workers.Reverse();
        RhinoApp.InvokeOnUiThread(
          (Action)
            delegate
            {
              ExpireSolution(recompute: true);
            }
        );
      }
    };
    Workers = new List<WorkerInstance>();
    CancellationSources = new List<CancellationTokenSource>();
    _tasks = new List<Task>();
  }

  protected override void BeforeSolveInstance()
  {
    if (_state != 0 && _setData == 1)
    {
      return;
    }

    foreach (CancellationTokenSource cancellationSource in CancellationSources)
    {
      cancellationSource.Cancel();
    }

    CancellationSources.Clear();
    Workers.Clear();
    _tasks.Clear();
    Interlocked.Exchange(ref _state, 0);
  }

  protected override void AfterSolveInstance()
  {
    if (_state != 0 || _tasks.Count <= 0 || _setData != 0)
    {
      return;
    }

    foreach (Task task in _tasks)
    {
      task.Start();
    }
  }

  protected override void ExpireDownStreamObjects()
  {
    if (_setData == 1)
    {
      base.ExpireDownStreamObjects();
    }
  }

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    if (_state == 0)
    {
      if (BaseWorker == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Worker class not provided.");
        return;
      }

      WorkerInstance currentWorker = BaseWorker.Duplicate();
      if (currentWorker == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not get a worker instance.");
        return;
      }

      currentWorker.GetData(dataAccess, Params);
      CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
      currentWorker.CancellationToken = cancellationTokenSource.Token;
      currentWorker.Id = $"Worker-{dataAccess.Iteration}";
      Task item = (
        TaskCreationOptions.HasValue
          ? new Task(
            delegate
            {
              currentWorker.DoWork(_done);
            },
            cancellationTokenSource.Token,
            TaskCreationOptions.Value
          )
          : new Task(
            delegate
            {
              currentWorker.DoWork(_done);
            },
            cancellationTokenSource.Token
          )
      );
      CancellationSources.Add(cancellationTokenSource);
      Workers.Add(currentWorker);
      _tasks.Add(item);
    }
    else if (_setData != 0)
    {
      if (Workers.Count > 0)
      {
        Interlocked.Decrement(ref _state);
        Workers[_state].SetData(dataAccess);
      }

      if (_state == 0)
      {
        CancellationSources.Clear();
        Workers.Clear();
        _tasks.Clear();
        Interlocked.Exchange(ref _setData, 0);
        Message = "Done";
        OnDisplayExpired(redraw: true);
      }
    }
  }

  public void RequestCancellation()
  {
    foreach (CancellationTokenSource cancellationSource in CancellationSources)
    {
      cancellationSource.Cancel();
    }

    CancellationSources.Clear();
    Workers.Clear();
    _tasks.Clear();
    Interlocked.Exchange(ref _state, 0);
    Interlocked.Exchange(ref _setData, 0);
    Message = "Cancelled";
    OnDisplayExpired(redraw: true);
  }
}
