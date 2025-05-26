using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Speckle.Connectors.GrasshopperShared.HostApp;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// An implementation of Param_GenericObject specific for variable input components that support name inheritance
/// </summary>
public class SpeckleVariableParam : Param_GenericObject
{
  public bool CanInheritNames { get; set; } = true;
  public bool AlwaysInheritNames { get; set; } = true;
  public override Guid ComponentGuid => new("A1B2C3D4-E5F6-7890-ABCD-123456789ABC");

  static SpeckleVariableParam()
  {
    // initialize KeyWatcher once for all instances
    KeyWatcher.Initialize();
  }

  public SpeckleVariableParam()
    : base() { }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu); // adds normal menu stuff first

    if (CanInheritNames && MutableNickName && Sources.Count > 0) // inherit names only shows up if something is connected
    {
      Menu_AppendSeparator(menu);
      Menu_AppendItem(menu, "Inherit names", (sender, args) => InheritNickname(), true);
    }
  }

  private void InheritNickname()
  {
    RecordUndoEvent("Input name change");
    var names = Sources.Select(s => s.NickName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
    var fullname = string.Join("|", names).Trim();
    var parentComponent = Attributes?.Parent?.DocObject as GH_Component;

    if (string.IsNullOrEmpty(fullname))
    {
      // if no valid names found, show a warning
      parentComponent?.AddRuntimeMessage(
        GH_RuntimeMessageLevel.Warning,
        $"Could not inherit name from parameter '{NickName}': No valid source names found."
      );
      return; // early return - no valid names to inherit
    }

    // valid names, proceed with inheritance
    Name = fullname;
    NickName = fullname;

    // expire the parent component to trigger updates
    if (parentComponent != null)
    {
      parentComponent.ExpireSolution(true);
    }
    else
    {
      // if standalone parameter, just expire preview
      ExpirePreview(true);
    }
  }

  public override void AddSource(IGH_Param source, int index)
  {
    base.AddSource(source, index); // do normal connection stuff

    // if Tab was pressed, automatically inherit name
    // or if set to always inherit
    if (MutableNickName && CanInheritNames && (AlwaysInheritNames || KeyWatcher.TabPressed))
    {
      InheritNickname();
    }
  }
}
