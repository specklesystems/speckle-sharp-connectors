using Grasshopper.Kernel;

namespace Speckle.Connectors.Grasshopper8;

public class SpeckleFirstComponent : GH_Component
{
  /// <summary>
  /// Each implementation of GH_Component must provide a public
  /// constructor without any arguments.
  /// Category represents the Tab in which the component will appear,
  /// Subcategory the panel. If you use non-existing tab or panel names,
  /// new tabs/panels will automatically be created.
  /// </summary>
  public SpeckleFirstComponent()
    : base("Speckle First Component", "SFC", "Description of component", "Speckle", "Test") { }

  /// <summary>
  /// Registers all the input parameters for this component.
  /// </summary>
  protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager) { }

  /// <summary>
  /// Registers all the output parameters for this component.
  /// </summary>
  protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager) { }

  /// <summary>
  /// This is the method that actually does the work.
  /// </summary>
  /// <param name="da">The DA object can be used to retrieve data from input parameters and
  /// to store data in output parameters.</param>
  protected override void SolveInstance(IGH_DataAccess da) { }

  /// <summary>
  /// Provides an Icon for every component that will be visible in the User Interface.
  /// Icons need to be 24x24 pixels.
  /// You can add image files to your project resources and access them like this:
  /// return Resources.IconForThisComponent;
  /// </summary>
  /// protected override System.Drawing.Bitmap Icon;

  /// <summary>
  /// Each component must have a unique Guid to identify it.
  /// It is vital this Guid doesn't change otherwise old ghx files
  /// that use the old ID will partially fail during loading.
  /// </summary>
  public override Guid ComponentGuid => new Guid("c123402d-6b40-4619-bb3b-88eb3fc8bb7a");
}
