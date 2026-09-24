using System.Runtime.InteropServices;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// Relates a Speckle object to others [ENG-9475]. A passthrough like Speckle Object: the source object comes out again
/// with the relations attached, and that output is what you publish. The relation type is picked from the button in
/// the component; both inputs rename themselves to the spec's direction for that type (Hosted → Host, Parent → Child,
/// …), so it is hard to wire an edge backwards. Chain components to give one object several relation types. Explore
/// shows the relations again after a load.
/// </summary>
/// <remarks>
/// Targets are held by application id, so they must be Speckle goos - a Speckle Object, Geometry or Block Instance. Raw
/// Rhino geometry mints a fresh id on every cast, so the same curve wired here and into Publish would never match.
/// </remarks>
[Guid("3B7C9E2D-5A41-4F8E-9D6C-1E0F2A8B7C54")]
public class SpeckleRelationComponent : GH_Component
{
  private const string RELATION_TYPE_KEY = "relationType";

  private SpeckleRelationType _type = SpeckleRelationType.HostedOn;

  public GhContextMenuButton TypeButton { get; }

  public SpeckleRelationComponent()
    : base(
      // display name only - Grasshopper binds by ComponentGuid, so this is cosmetic and safe to change
      "Speckle Relation",
      "SR",
      "Relate a Speckle object to others. The object comes out with the relations attached - publish that. Explore reads them back after a load.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    )
  {
    TypeButton = new GhContextMenuButton(Info.Label, Info.SpecName, Info.Description, PopulateTypeMenu);
    ApplyTypeToButton();
  }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_query;
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  private SpeckleRelationTypeInfo Info => SpeckleRelationTypes.Info(_type);

  public override void CreateAttributes() => m_attributes = new SpeckleRelationComponentAttributes(this);

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var info = Info;
    // nicknames ARE the full words: the direction is the point, and a one-letter nick hides it
    pManager.AddGenericParameter(info.SourceName, info.SourceName, info.SourceDescription, GH_ParamAccess.item);
    pManager.AddGenericParameter(info.TargetName, info.TargetName, info.TargetDescription, GH_ParamAccess.list);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    var info = Info;
    pManager.AddGenericParameter(
      info.SourceName,
      info.SourceName,
      "The source object with the relation(s) attached. Publish this, not the original.",
      GH_ParamAccess.item
    );
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    IGH_Goo? sourceGoo = null;
    List<IGH_Goo?> targetGoos = new();
    if (!da.GetData(0, ref sourceGoo) || !da.GetDataList(1, targetGoos))
    {
      return;
    }

    var info = Info;
    if (ToWrapper(sourceGoo, info.SourceName) is not { } source)
    {
      return;
    }

    // deep copy so the canvas object upstream is never mutated - same rule as every other passthrough
    SpeckleWrapper copy = source switch
    {
      SpeckleDataObjectWrapper dataObject => dataObject.DeepCopy(),
      SpeckleGeometryWrapper geometry => geometry.DeepCopy(), // virtual: a block instance copies as a block instance
      _ => throw new InvalidOperationException($"{source.GetType().Name} cannot carry relations"),
    };

    var relations = new List<SpeckleRelation>(copy.Relations);
    foreach (var targetGoo in targetGoos)
    {
      if (targetGoo is null)
      {
        continue; // a null in the target list is a hole, not a target
      }
      if (ToWrapper(targetGoo, info.TargetName) is not { } target)
      {
        return; // ToWrapper said why
      }
      if (string.IsNullOrEmpty(target.ApplicationId))
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"A {info.TargetName} object has no application id, so nothing can point at it."
        );
        return;
      }
      if (
        !string.IsNullOrEmpty(copy.ApplicationId)
        && string.Equals(copy.ApplicationId, target.ApplicationId, StringComparison.Ordinal)
      )
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Skipped a relation from an object to itself.");
        continue;
      }

      var relation = new SpeckleRelation
      {
        Type = _type,
        TargetId = target.ApplicationId!,
        TargetName = string.IsNullOrWhiteSpace(target.Name) ? null : target.Name,
      };
      if (!relations.Any(existing => existing.SameEdgeAs(relation)))
      {
        relations.Add(relation);
      }
    }

    copy.Relations = relations;
    da.SetData(0, copy.CreateGoo());
  }

  /// <summary>
  /// The wrapper behind a Speckle goo. Only wrappers qualify: they carry their application id through wires and deep
  /// copies, so what a relation records is what Publish interns. Anything else gets told why.
  /// </summary>
  private SpeckleWrapper? ToWrapper(IGH_Goo? goo, string endName)
  {
    switch (goo)
    {
      case SpeckleBlockInstanceWrapperGoo instance:
        return instance.Value;
      case SpeckleDataObjectWrapperGoo dataObject:
        return dataObject.Value;
      case SpeckleGeometryWrapperGoo geometry:
        return geometry.Value;
      case SpeckleCollectionWrapperGoo:
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"{endName} is a collection. Collections are containers, not objects - relate the objects inside it."
        );
        return null;
      case null:
        return null;
      default:
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"{endName} is a {goo.TypeName}, not a Speckle object. Pass it through Speckle Object or Speckle Geometry first so it keeps one application id."
        );
        return null;
    }
  }

  // ── relation type: button + persisted state ─────────────────────────────────────────────────────────────

  private bool PopulateTypeMenu(ToolStripDropDown menu)
  {
    foreach (var info in SpeckleRelationTypes.All)
    {
      var item = new ToolStripMenuItem(info.Label) { Checked = info.Type == _type, ToolTipText = info.Description };
      var type = info.Type;
      item.Click += (_, _) => SelectType(type);
      menu.Items.Add(item);
    }
    return true;
  }

  private void SelectType(SpeckleRelationType type)
  {
    if (type == _type)
    {
      return;
    }

    RecordUndoEvent("Relation type");
    _type = type;
    ApplyType();
    ExpireSolution(true);
  }

  /// <summary>Renames the inputs, output and button to the current type. Params keep their wires.</summary>
  private void ApplyType()
  {
    var info = Info;
    if (Params.Input.Count >= 2)
    {
      SetEnd(Params.Input[0], info.SourceName, info.SourceDescription);
      SetEnd(Params.Input[1], info.TargetName, info.TargetDescription);
    }
    if (Params.Output.Count >= 1)
    {
      SetEnd(Params.Output[0], info.SourceName, Params.Output[0].Description);
    }
    ApplyTypeToButton();
    Params.OnParametersChanged();
    Attributes?.ExpireLayout();
    OnDisplayExpired(true);
  }

  private void ApplyTypeToButton()
  {
    var info = Info;
    TypeButton.Name = info.Label;
    TypeButton.NickName = info.SpecName;
    TypeButton.Description = $"{info.Description}\n\nLeft-click to pick another relation type.";
  }

  private static void SetEnd(IGH_Param param, string name, string description)
  {
    param.Name = name;
    param.NickName = name;
    param.Description = description;
  }

  public override bool Write(GH_IWriter writer)
  {
    writer.SetInt32(RELATION_TYPE_KEY, (int)_type);
    return base.Write(writer);
  }

  public override bool Read(GH_IReader reader)
  {
    int stored = 0;
    if (reader.TryGetInt32(RELATION_TYPE_KEY, ref stored) && SpeckleRelationTypes.IsAuthorable(stored))
    {
      _type = (SpeckleRelationType)stored;
    }

    bool result = base.Read(reader);
    ApplyType(); // the file restores whatever the params were called; the type decides what they are called now
    return result;
  }
}
