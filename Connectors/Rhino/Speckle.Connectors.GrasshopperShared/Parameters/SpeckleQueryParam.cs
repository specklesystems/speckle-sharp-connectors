using System.Reflection;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleQueryParam : Param_GenericObject
{
  public override GH_Exposure Exposure => GH_Exposure.hidden;
  public override Guid ComponentGuid => new("1259E575-A878-445D-9B86-6E325979EBF2");
  public ComparisonMethod ComparisonMethod { get; set; } = ComparisonMethod.Default;

  public override GH_StateTagList StateTags
  {
    get
    {
      var tags = base.StateTags;
      if (Kind == GH_ParamKind.input)
      {
        tags.Add(new WildcardStateTag());
        tags.Add(new EqualStateTag());
      }

      return tags;
    }
  }

  protected void SetWildcard(bool state)
  {
    if (state)
    {
      ComparisonMethod = ComparisonMethod.Wildcard;
    }

    HandleParamStateChange();
  }

  protected void SetEquality(bool state)
  {
    if (state)
    {
      ComparisonMethod = ComparisonMethod.Equality;
    }

    HandleParamStateChange();
  }

  private void HandleParamStateChange()
  {
    OnObjectChanged(GH_ObjectEventType.DataMapping);
    OnDisplayExpired(true);
    ExpireSolution(true);
  }

  public override bool Write(GH_IWriter writer)
  {
    writer.SetString("comparison", ComparisonMethod.ToString());
    return base.Write(writer);
  }

  public override bool Read(GH_IReader reader)
  {
    string comparisonMethodString = "";
    reader.TryGetString("comparison", ref comparisonMethodString);

    ComparisonMethod = Enum.TryParse<ComparisonMethod>(comparisonMethodString, out ComparisonMethod storedMethod)
      ? storedMethod
      : ComparisonMethod.Default;
    ;

    return base.Read(reader);
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    if (Kind != GH_ParamKind.input)
    {
      // Append graft,flatten,etc... options to outputs.
      base.AppendAdditionalMenuItems(menu);
      if (Kind == GH_ParamKind.output)
      {
        Menu_AppendExtractParameter(menu);
      }

      return;
    }

    Menu_AppendSeparator(menu);

    Menu_AppendCustomMenuItems(menu);

    Menu_AppendSeparator(menu);

    base.AppendAdditionalMenuItems(menu);
  }

  protected virtual void Menu_AppendCustomMenuItems(ToolStripDropDown menu) { }

  protected void Menu_AppendWildcardToggle(ToolStripDropDown menu)
  {
    var detachToggle = Menu_AppendItem(
      menu,
      "Wildcard",
      (s, e) => SetWildcard(ComparisonMethod != ComparisonMethod.Wildcard),
      true,
      ComparisonMethod == ComparisonMethod.Wildcard
    );
    detachToggle.ToolTipText = "Use the wildcard comparison method for this input.";
    detachToggle.Image = Resources.speckle_query_wildcard;
  }

  protected void Menu_AppendEqualityToggle(ToolStripDropDown menu)
  {
    var optionalToggle = Menu_AppendItem(
      menu,
      "Equality",
      (sender, args) => SetEquality(ComparisonMethod != ComparisonMethod.Equality),
      true,
      ComparisonMethod == ComparisonMethod.Equality
    );
    optionalToggle.ToolTipText = "Use the equality comparison method for this input.";
    optionalToggle.Image = Resources.speckle_query_equals;
  }
}

public abstract class ComponentQueryTag : GH_StateTag
{
  public override string Description { get; }
  public override string Name { get; }
  public override Bitmap? Icon { get; }
  public abstract ComparisonMethod Method { get; }
}

public class WildcardStateTag : ComponentQueryTag
{
  public override string Description => "This parameter is set to wildcard comparison";
  public override string Name => "Wildcard Comparer";
  public override Bitmap? Icon
  {
    get
    {
      Assembly assembly = GetType().Assembly;
      var stream = assembly.GetManifestResourceStream(
        assembly.GetName().Name + "." + "Resources" + ".speckle_query_wildcard.png"
      );
      return stream != null ? new Bitmap(stream) : null;
    }
  }
  public override ComparisonMethod Method => ComparisonMethod.Wildcard;
}

public class EqualStateTag : ComponentQueryTag
{
  public override string Description => "This parameter is set to equality comparison";
  public override string Name => "Equality Comparer";
  public override Bitmap? Icon
  {
    get
    {
      Assembly assembly = GetType().Assembly;
      var stream = assembly.GetManifestResourceStream(
        assembly.GetName().Name + "." + "Resources" + ".speckle_query_equals.png"
      );
      return stream != null ? new Bitmap(stream) : null;
    }
  }
  public override ComparisonMethod Method => ComparisonMethod.Equality;
}

public enum ComparisonMethod
{
  Equality,
  Wildcard,
  Default
}
