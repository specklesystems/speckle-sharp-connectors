using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.HostApp.Extras;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Components.Collections;

#pragma warning disable CA1711
public class CreateCollection : GH_Component, IGH_VariableParameterComponent
#pragma warning restore CA1711
{
  public override Guid ComponentGuid => new("BDCE743E-7BDB-479B-AA81-19854AB5A254");
  protected override Bitmap Icon => Resources.speckle_collections_create;

  private readonly DebounceDispatcher _debounceDispatcher = new();

  private bool _alwaysInheritNames;

  public bool AlwaysInheritNames
  {
    get => _alwaysInheritNames;
    set
    {
      _alwaysInheritNames = value;

      UpdateMessage();
    }
  }

  public CreateCollection()
    : base(
      "Create Collection",
      "cC",
      "Creates a new Collection",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.COLLECTIONS
    )
  {
    UpdateMessage();
  }

  private void UpdateMessage()
  {
    Message = AlwaysInheritNames ? "Inheriting nicknames" : "";
  }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var p = CreateParameter(GH_ParameterSide.Input, 0);
    pManager.AddParameter(p);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Collection", "C", "Created parent collection", GH_ParamAccess.tree);
  }

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    string rootName = "Unnamed";
    Collection rootCollection = new();
    SpeckleCollectionWrapper rootSpeckleCollectionWrapper =
      new()
      {
        Base = rootCollection,
        Name = rootName,
        Path = new() { rootName },
        Color = null,
        Material = null,
        ApplicationId = InstanceGuid.ToString()
      };

    foreach (var inputParam in Params.Input)
    {
      var data = inputParam.VolatileData.AllData(true).ToList();
      if (data.Count == 0)
      {
        continue;
      }

      var inputCollections = data.OfType<SpeckleCollectionWrapperGoo>()
        .Empty()
        .Select(o => (SpeckleCollectionWrapperGoo)o.Duplicate())
        .ToList();
      var inputNonCollections = data.Where(t => t is not SpeckleCollectionWrapperGoo).Empty().ToList();

      if (inputCollections.Count != 0 && inputNonCollections.Count != 0)
      {
        // error out! we want to disallow setting objects and collections in the same parent collection
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"Parameter {inputParam.NickName} should not contain both objects and collections."
        );
        return;
      }

      List<string> childPath = new() { rootName };
      childPath.Add(inputParam.NickName);
      SpeckleCollectionWrapper childSpeckleCollectionWrapper =
        new()
        {
          Base = new Collection(),
          Name = inputParam.NickName,
          Path = childPath,
          Color = null,
          Material = null,
          Topology = GrasshopperHelpers.GetParamTopology(inputParam),
          ApplicationId = inputParam.InstanceGuid.ToString(),
        };

      // handle collection inputs
      // if on this port we're only receiving collections, we should become "pass-through" to not create
      // needless nesting
      if (inputCollections.Count == data.Count)
      {
        var nameTest = new HashSet<string>();
        foreach (SpeckleCollectionWrapperGoo wrapperGoo in inputCollections)
        {
          // update the speckle collection path
          wrapperGoo.Value.Path = childPath;

          foreach (
            string subCollectionName in wrapperGoo.Value.Elements.OfType<SpeckleCollectionWrapper>().Select(c => c.Name)
          )
          {
            var hasNotSeenNameBefore = nameTest.Add(subCollectionName);
            if (!hasNotSeenNameBefore)
            {
              AddRuntimeMessage(
                GH_RuntimeMessageLevel.Error,
                $"Duplicate collection name found: {subCollectionName} in input parameter {inputParam.NickName}. Please ensure collection names are unique per nesting level.\n See https://speckle.docs/grashopper/collections"
              );
              return;
            }
          }

          childSpeckleCollectionWrapper.Elements.AddRange(wrapperGoo.Value.Elements);
        }

        rootSpeckleCollectionWrapper.Elements.Add(childSpeckleCollectionWrapper);
        continue;
      }

      // handle object inputs
      foreach (var obj in inputNonCollections)
      {
        SpeckleObjectWrapperGoo wrapperGoo = new();
        if (wrapperGoo.CastFrom(obj))
        {
          wrapperGoo.Value.Path = childPath;
          wrapperGoo.Value.Parent = childSpeckleCollectionWrapper;
          childSpeckleCollectionWrapper.Elements.Add(wrapperGoo.Value);
        }
      }

      rootSpeckleCollectionWrapper.Elements.Add(childSpeckleCollectionWrapper);
    }

    dataAccess.SetData(0, new SpeckleCollectionWrapperGoo(rootSpeckleCollectionWrapper));
  }

  public bool CanInsertParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input;

  public bool CanRemoveParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input;

  public IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    var myParam = new SpeckleVariableParam
    {
      Name = $"Sub-Collection {Params.Input.Count + 1}",
      NickName = $"Sub-Collection {Params.Input.Count + 1}",
      MutableNickName = true,
      Optional = true,
      Access = GH_ParamAccess.tree,
      CanInheritNames = true,
      AlwaysInheritNames = AlwaysInheritNames
    };

    return myParam;
  }

  public bool DestroyParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input;

  public override void AddedToDocument(GH_Document document)
  {
    base.AddedToDocument(document);
    Params.ParameterChanged += (sender, args) =>
    {
      if (args.ParameterSide == GH_ParameterSide.Output)
      {
        return;
      }
      switch (args.OriginalArguments.Type)
      {
        case GH_ObjectEventType.NickName:
          // This means the user is typing characters, debounce until it stops for 400ms before expiring the solution.
          // Prevents UI from locking too soon while writing new names for inputs.
          args.Parameter.Name = args.Parameter.NickName;
          _debounceDispatcher.Debounce(500, e => ExpireSolution(true));
          break;
        case GH_ObjectEventType.NickNameAccepted:
          args.Parameter.Name = args.Parameter.NickName;
          ExpireSolution(true);
          break;
        case GH_ObjectEventType.Sources:
          // if this event is a source change, and param is the last input, then add a new param automatically
          if (args.Parameter.SourceCount > 0 && args.ParameterIndex == Params.Input.Count - 1)
          {
            IGH_Param param = CreateParameter(GH_ParameterSide.Input, Params.Input.Count);
            Params.RegisterInputParam(param);
            Params.OnParametersChanged();
          }
          break;
      }
    };
  }

  public void VariableParameterMaintenance()
  {
    //todo
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);
    ToolStripMenuItem alwaysInheritMenuItem = Menu_AppendItem(
      menu,
      "Always inherit names",
      (s, e) =>
      {
        AlwaysInheritNames = !AlwaysInheritNames;
        // Update existing parameters
        foreach (var param in Params.Input.OfType<SpeckleVariableParam>())
        {
          param.AlwaysInheritNames = AlwaysInheritNames;
        }
        OnDisplayExpired(true);
      },
      true,
      AlwaysInheritNames
    );
    alwaysInheritMenuItem.ToolTipText =
      "Toggle automatic name inheritance. If set, parameters will automatically inherit names from connected sources.";
  }
}
