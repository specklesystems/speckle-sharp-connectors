using Grasshopper.Kernel;

namespace Speckle.Connectors.Grasshopper8.Components.BaseComponents;

public abstract class WorkerInstance
{
  public GH_Component Parent { get; set; }

  public CancellationToken CancellationToken { get; set; }

  public string Id { get; set; }

  protected WorkerInstance(GH_Component parent)
  {
    Parent = parent;
  }

  public abstract WorkerInstance Duplicate();

  public abstract void DoWork(Action done);

  public abstract void SetData(IGH_DataAccess dataAccess);

  public abstract void GetData(IGH_DataAccess dataAccess, GH_ComponentParamServer p);
}
