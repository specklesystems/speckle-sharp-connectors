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

  public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
  {
    if (!Owner.Enabled && e.Button == MouseButtons.Right)
    {
      // Prevents canvas from triggering the right-click behaviour, and showing the context menu.
      return GH_ObjectResponse.Handled;
    }

    // Allowing event to bubble up to canvas will handle the event and show the context menu.
    return base.RespondToMouseUp(sender, e);
  }
}
