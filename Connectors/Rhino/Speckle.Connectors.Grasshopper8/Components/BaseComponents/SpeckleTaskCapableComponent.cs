using Grasshopper.Kernel;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Sdk;

namespace Speckle.Connectors.Grasshopper8.Components.BaseComponents;

public abstract class SpeckleTaskCapableComponent<TInput, TOutput>(
  string name,
  string nickname,
  string description,
  string category,
  string subCategory
) : GH_TaskCapableComponent<TOutput>(name, nickname, description, category, subCategory)
{
  protected override void SolveInstance(IGH_DataAccess da)
  {
    //TODO: We're missing activity and logging here. Will enable it for all inherited classes.

    if (InPreSolve)
    {
      // Collect the data and create the task
      try
      {
        var input = GetInput(da);
        TaskList.Add(PerformTask(input, CancelToken));
      }
      catch (SpeckleException e)
      {
        Console.WriteLine(e);
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
      }
      return;
    }

    bool solveResults = false;
    TOutput? result = default;

    try
    {
      solveResults = GetSolveResults(da, out result);
    }
    catch (AggregateException e)
    {
      Console.WriteLine(e);
      foreach (var inner in e.InnerExceptions)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, inner.Message);
      }
    }

    if (!solveResults)
    {
      // INFO: This will run synchronously. Useful for Rhino.Compute runs, but can also be enabled by user.
      try
      {
        TInput input = GetInput(da);
        var syncResult = PerformTask(input).Result;
        result = syncResult;
      }
      catch (AggregateException e)
      {
        foreach (var inner in e.InnerExceptions)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, inner.Message);
        }
        return;
      }
    }

    if (result is not null)
    {
      SetOutput(da, result);
    }
  }

  protected override Bitmap Icon => BitmapBuilder.CreateSquareIconBitmap(IconText);

  protected string IconText => string.Join("", Name.Split(' ').Select(s => s.First()));

  protected abstract TInput GetInput(IGH_DataAccess da);

  protected abstract void SetOutput(IGH_DataAccess da, TOutput result);

  protected abstract Task<TOutput> PerformTask(TInput input, CancellationToken cancellationToken = default);
}
