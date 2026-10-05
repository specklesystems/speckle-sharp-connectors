using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>Draws the relation-type button under the component body, the way Speckle Model draws its pickers.</summary>
public class SpeckleRelationComponentAttributes : GH_ComponentAttributes
{
  private const int BUTTON_HEIGHT = 26;

  private readonly SpeckleRelationComponent _typedOwner;

  public SpeckleRelationComponentAttributes(IGH_Component component)
    : base(component)
  {
    _typedOwner = (SpeckleRelationComponent)component;
  }

  public override void AppendToAttributeTree(List<IGH_Attributes> attributes)
  {
    base.AppendToAttributeTree(attributes);
    _typedOwner.TypeButton.Attributes?.AppendToAttributeTree(attributes);
  }

  private void InitialiseAttributes()
  {
    _typedOwner.TypeButton.Attributes ??= new GhContextMenuButtonAttributes(_typedOwner.TypeButton)
    {
      Parent = this,
      Pivot = Pivot,
    };
  }

  protected override void Layout()
  {
    base.Layout();
    var baseRec = GH_Convert.ToRectangle(Bounds);
    baseRec.Height += BUTTON_HEIGHT;

    var btnRec = baseRec;
    btnRec.Y = baseRec.Bottom - BUTTON_HEIGHT;
    btnRec.Height = BUTTON_HEIGHT;
    btnRec.Inflate(-2, -2);

    Bounds = baseRec;
    InitialiseAttributes();
    // Both pivot and bounds require updating to properly render the button in place
    _typedOwner.TypeButton.Attributes.Pivot = btnRec.Location;
    _typedOwner.TypeButton.Attributes.Bounds = btnRec;
  }

  protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
  {
    base.Render(canvas, graphics, channel);
    _typedOwner.TypeButton.Attributes.RenderToCanvas(canvas, channel);
  }
}
