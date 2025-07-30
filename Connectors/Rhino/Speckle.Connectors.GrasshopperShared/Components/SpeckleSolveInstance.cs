using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;

namespace Speckle.Connectors.GrasshopperShared.Components;

public abstract class SpeckleSolveInstance(
  string name,
  string nickname,
  string description,
  string category,
  string subCategory
) : GH_Component(name, nickname, description, category, subCategory)
{
  protected override void BeforeSolveInstance() => SpeckleConversionContext.SetupCurrent();

  protected override void AfterSolveInstance() => SpeckleConversionContext.EndCurrent();

  protected abstract override void SolveInstance(IGH_DataAccess da);
}
