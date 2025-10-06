using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Parameters;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Simple extension of Param_GenericObject that adds "Extract parameter" functionality.
/// Follows the existing v3 codebase patterns.
/// </summary>
public class SpeckleOutputParam : Param_GenericObject
{
  public override GH_Exposure Exposure => GH_Exposure.hidden;
  public override Guid ComponentGuid => new("D2B4713D-FE8B-4EF0-8445-B6096DB15B24");

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);

    // only show extract parameter option for output parameters that have no connections
    if (Kind == GH_ParamKind.output && Recipients.Count == 0)
    {
      Menu_AppendSeparator(menu);
      Menu_AppendItem(menu, "Extract parameter", Menu_ExtractOutputParameterClicked, true);
    }
  }

  /// <summary>
  /// Extract parameter implementation - taken from v2 legacy and simplified for v3.
  /// </summary>
  private void Menu_ExtractOutputParameterClicked(object sender, EventArgs e)
  {
    var archive = new GH_Archive();
    if (!archive.AppendObject(this, "Parameter"))
    {
      return;
    }

    var newParam = new SpeckleOutputParam();
    newParam.CreateAttributes();

    if (!archive.ExtractObject(newParam, "Parameter"))
    {
      return;
    }

    newParam.NewInstanceGuid();
    newParam.Attributes.Selected = false;
    newParam.Attributes.PerformLayout();
    newParam.Attributes.Pivot = new PointF(
      Attributes.Parent.Bounds.Right + newParam.Attributes.Bounds.Width * 0.5f + 15,
      Attributes.Pivot.Y
    );
    newParam.MutableNickName = true;

    if (newParam.Attributes is GH_FloatingParamAttributes floating)
    {
      floating.PerformLayout();
    }

    var document = OnPingDocument();
    if (document != null)
    {
      document.AddObject(newParam, false);
      newParam.AddSource(this);
      newParam.ExpireSolution(true);
    }
  }
}
