using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Speckle.Connectors.Grasshopper8.Components.Operations;

public class SpeckleSelectModelComponentAttributes : GH_ComponentAttributes
{
  private readonly SpeckleSelectModelComponent _typedOwner;

  public SpeckleSelectModelComponentAttributes(IGH_Component component)
    : base(component)
  {
    _typedOwner = (SpeckleSelectModelComponent)component;
  }

  public override void AppendToAttributeTree(List<IGH_Attributes> attributes)
  {
    base.AppendToAttributeTree(attributes);
    _typedOwner.ProjectContextMenuButton.Attributes?.AppendToAttributeTree(attributes);
    _typedOwner.ModelContextMenuButton.Attributes?.AppendToAttributeTree(attributes);
    _typedOwner.VersionContextMenuButton.Attributes?.AppendToAttributeTree(attributes);
  }

  private void InitialiseAttributes()
  {
    if (_typedOwner.ProjectContextMenuButton.Attributes == null)
    {
      _typedOwner.ProjectContextMenuButton.Attributes = new GhContextMenuButtonAttributes(
        _typedOwner.ProjectContextMenuButton
      )
      {
        Parent = this,
      };
    }

    if (_typedOwner.ModelContextMenuButton.Attributes == null)
    {
      _typedOwner.ModelContextMenuButton.Attributes = new GhContextMenuButtonAttributes(
        _typedOwner.ModelContextMenuButton
      )
      {
        Parent = this,
        Pivot = Pivot
      };
    }

    if (_typedOwner.VersionContextMenuButton.Attributes == null)
    {
      _typedOwner.VersionContextMenuButton.Attributes = new GhContextMenuButtonAttributes(
        _typedOwner.VersionContextMenuButton
      )
      {
        Parent = this,
        Pivot = Pivot
      };
    }
  }

  protected override void Layout()
  {
    base.Layout();
    var baseRec = GH_Convert.ToRectangle(Bounds);
    baseRec.Height += 26 * 3;

    var btnRec = baseRec;
    btnRec.Y = baseRec.Bottom - 26 * 3;
    btnRec.Height = 26;
    btnRec.Inflate(-2, -2);

    var btnRec2 = btnRec;
    btnRec2.Y = btnRec.Bottom + 2;

    var btnRec3 = btnRec;
    btnRec3.Y = btnRec2.Bottom + 2;

    Bounds = baseRec;
    InitialiseAttributes();
    // Both pivot and bounds require updating to proper render buttons on location
    _typedOwner.ProjectContextMenuButton.Attributes.Pivot = btnRec.Location;
    _typedOwner.ProjectContextMenuButton.Attributes.Bounds = btnRec;
    _typedOwner.ModelContextMenuButton.Attributes.Pivot = btnRec2.Location;
    _typedOwner.ModelContextMenuButton.Attributes.Bounds = btnRec2;
    _typedOwner.VersionContextMenuButton.Attributes.Pivot = btnRec3.Location;
    _typedOwner.VersionContextMenuButton.Attributes.Bounds = btnRec3;
  }

  protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
  {
    base.Render(canvas, graphics, channel);
    // Draw custom buttons and dropdowns

    _typedOwner.ProjectContextMenuButton.Attributes.RenderToCanvas(canvas, channel);
    _typedOwner.ModelContextMenuButton.Attributes.RenderToCanvas(canvas, channel);
    _typedOwner.VersionContextMenuButton.Attributes.RenderToCanvas(canvas, channel);
  }
}
