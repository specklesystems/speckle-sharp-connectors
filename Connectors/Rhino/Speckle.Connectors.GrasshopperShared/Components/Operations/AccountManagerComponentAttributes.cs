using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class AccountManagerComponentAttributes : GH_ComponentAttributes
{
  private readonly AccountManagerComponent _typedOwner;

  public AccountManagerComponentAttributes(IGH_Component component)
    : base(component)
  {
    _typedOwner = (AccountManagerComponent)component;
  }

  public override void AppendToAttributeTree(List<IGH_Attributes> attributes)
  {
    base.AppendToAttributeTree(attributes);
    _typedOwner.SignInButton.Attributes?.AppendToAttributeTree(attributes);
  }

  private void InitialiseAttributes()
  {
    _typedOwner.SignInButton.Attributes ??= new GhContextMenuButtonAttributes(_typedOwner.SignInButton)
    {
      Parent = this,
    };
  }

  protected override void Layout()
  {
    base.Layout();
    var baseRec = GH_Convert.ToRectangle(Bounds);
    baseRec.Height += 26;

    var btnRec = baseRec;
    btnRec.Y = baseRec.Bottom - 26;
    btnRec.Height = 26;
    btnRec.Inflate(-2, -2);

    Bounds = baseRec;
    InitialiseAttributes();

    _typedOwner.SignInButton.Attributes.Pivot = btnRec.Location;
    _typedOwner.SignInButton.Attributes.Bounds = btnRec;
  }

  protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
  {
    base.Render(canvas, graphics, channel);
    _typedOwner.SignInButton.Attributes.RenderToCanvas(canvas, channel);
  }
}
