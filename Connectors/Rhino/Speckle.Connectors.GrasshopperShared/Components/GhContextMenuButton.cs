using Grasshopper.Kernel;

namespace Speckle.Connectors.GrasshopperShared.Components;

public class GhContextMenuButton(
  string name,
  string nickname,
  string description,
  Func<ToolStripDropDown, bool>? populateMenuAction = null
) : GH_DocumentObject(name, nickname, description, "Speckle", "UI")
{
  public bool Enabled { get; set; } = true;

  public override void CreateAttributes() => Attributes = new GhContextMenuButtonAttributes(this);

  public override Guid ComponentGuid => new("B01FFD91-F4EC-4332-A9AA-F917AEDAA51D");

  public override bool AppendMenuItems(ToolStripDropDown menu) => populateMenuAction?.Invoke(menu) ?? false;
}
