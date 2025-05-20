using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace Speckle.Connectors.GrasshopperShared.Components;

public class GhContextMenuButtonAttributes(GhContextMenuButton owner) : GH_Attributes<GhContextMenuButton>(owner)
{
  protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
  {
    base.Render(canvas, graphics, channel);
    if (channel != GH_CanvasChannel.Objects)
    {
      return; // No wires or other layers are being drawn in this component.
    }

    using var button1 = GH_Capsule.CreateTextCapsule(
      Bounds,
      Bounds,
      Owner.Enabled ? GH_Palette.Black : GH_Palette.Grey,
      Owner.Name,
      2,
      0
    );
    button1.Render(graphics, Parent.Selected, false, false);
  }

  public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
  {
    if (Bounds.Contains(e.CanvasLocation) && e.Button == MouseButtons.Left)
    {
      // handle the mouse down to prevent component selection
      return GH_ObjectResponse.Handled;
    }

    return base.RespondToMouseDown(sender, e);
  }

  public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
  {
    // detect left-clicks on enabled buttons
    if (Owner.Enabled && e.Button == MouseButtons.Left && Bounds.Contains(e.CanvasLocation))
    {
      // show menu
      ToolStripDropDown menu = new();
      Owner.AppendMenuItems(menu);
      menu.Show(sender, sender.PointToClient(Cursor.Position));
      return GH_ObjectResponse.Handled;
    }

    // block right-clicks to prevent the default context menu
    if (e.Button == MouseButtons.Right && Bounds.Contains(e.CanvasLocation))
    {
      return GH_ObjectResponse.Handled;
    }

    return base.RespondToMouseUp(sender, e);
  }
}
