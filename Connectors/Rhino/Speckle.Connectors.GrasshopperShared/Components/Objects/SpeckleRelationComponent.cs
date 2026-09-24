using System.Runtime.InteropServices;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// Relates two Speckle objects [ENG-9475]. The relation type is picked from the button in the component; the two inputs
/// rename themselves to the spec's direction for that type (Hosted → Host, Parent → Child, …), so it is hard to wire
/// an edge backwards. The output is a relation goo: wire it into Publish next to the objects it names, and Explore
/// shows it again after a load.
/// </summary>
/// <remarks>
/// Ends are held by application id, so they must be Speckle goos - a Speckle Object, Geometry or Block Instance. Raw
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
      "Relate two Speckle objects. Publish the relation alongside the objects; Explore reads it back after a load.",
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
    pManager.AddGenericParameter(info.SourceName, info.SourceNickName, info.SourceDescription, GH_ParamAccess.item);
    pManager.AddGenericParameter(info.TargetName, info.TargetNickName, info.TargetDescription, GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleRelationParam(),
      "Relation",
      "R",
      "The relation. Wire it into Publish together with the objects it relates.",
      GH_ParamAccess.item
    );
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    IGH_Goo? source = null;
    IGH_Goo? target = null;
    if (!da.GetData(0, ref source) || !da.GetData(1, ref target))
    {
      return;
    }

    var info = Info;
    if (
      !TryGetEnd(source, info.SourceName, out string sourceId, out string? sourceName)
      || !TryGetEnd(target, info.TargetName, out string targetId, out string? targetName)
    )
    {
      return;
    }

    if (string.Equals(sourceId, targetId, StringComparison.Ordinal))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "An object cannot be related to itself.");
      return;
    }

    da.SetData(
      0,
      new SpeckleRelationGoo(
        new SpeckleRelation
        {
          Type = _type,
          SourceId = sourceId,
          TargetId = targetId,
          SourceName = sourceName,
          TargetName = targetName,
        }
      )
    );
  }

  /// <summary>
  /// The application id a Speckle goo will publish under. Only wrappers qualify: they carry their id through wires
  /// and deep copies, so what this component records is what Publish interns.
  /// </summary>
  private bool TryGetEnd(IGH_Goo? goo, string endName, out string id, out string? name)
  {
    id = "";
    name = null;
    string? appId;
    switch (goo)
    {
      case SpeckleBlockInstanceWrapperGoo instance:
        appId = instance.Value.ApplicationId;
        name = instance.Value.Name;
        break;
      case SpeckleDataObjectWrapperGoo dataObject:
        appId = dataObject.Value.ApplicationId;
        name = dataObject.Value.Name;
        break;
      case SpeckleGeometryWrapperGoo geometry:
        appId = geometry.Value.ApplicationId;
        name = geometry.Value.Name;
        break;
      case SpeckleCollectionWrapperGoo:
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"{endName} is a collection. Collections are containers, not objects - relate the objects inside it."
        );
        return false;
      case null:
        return false;
      default:
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"{endName} is a {goo.TypeName}, not a Speckle object. Pass it through Speckle Object or Speckle Geometry first so it keeps one application id."
        );
        return false;
    }

    if (string.IsNullOrEmpty(appId))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"{endName} has no application id, so it cannot be related.");
      return false;
    }

    id = appId!;
    if (string.IsNullOrWhiteSpace(name))
    {
      name = null;
    }
    return true;
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

  /// <summary>Renames the two inputs and the button to the current type. Inputs keep their wires.</summary>
  private void ApplyType()
  {
    var info = Info;
    if (Params.Input.Count >= 2)
    {
      SetEnd(Params.Input[0], info.SourceName, info.SourceNickName, info.SourceDescription);
      SetEnd(Params.Input[1], info.TargetName, info.TargetNickName, info.TargetDescription);
    }
    ApplyTypeToButton();
    Params.OnParametersChanged();
    Attributes?.ExpireLayout();
  }

  private void ApplyTypeToButton()
  {
    var info = Info;
    TypeButton.Name = info.Label;
    TypeButton.NickName = info.SpecName;
    TypeButton.Description = $"{info.Description}\n\nLeft-click to pick another relation type.";
  }

  private static void SetEnd(IGH_Param param, string name, string nickName, string description)
  {
    param.Name = name;
    param.NickName = nickName;
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
    ApplyType(); // the file restores whatever the inputs were called; the type decides what they are called now
    return result;
  }
}
