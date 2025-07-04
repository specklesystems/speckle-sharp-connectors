using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Components.Collections;

/// <summary>
/// Simplified CreateCollection component using the base class pattern
/// </summary>
#pragma warning disable CA1711
public class CreateCollection : VariableParameterComponentBase
#pragma warning restore CA1711
{
  public override Guid ComponentGuid => new("BDCE743E-7BDB-479B-AA81-19854AB5A254");
  protected override Bitmap Icon => Resources.speckle_collections_create;

  public CreateCollection()
    : base(
      "Create Collection",
      "cC",
      "Creates a new Collection",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.COLLECTIONS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var param = CreateParameter(GH_ParameterSide.Input, 0);
    pManager.AddParameter(param);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionParam(),
      "Collection",
      "C",
      "Created parent collection",
      GH_ParamAccess.item
    );
  }

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    var rootCollection = CreateRootCollection();

    foreach (var inputParam in Params.Input)
    {
      var data = inputParam.VolatileData.AllData(true).ToList();
      if (data.Count == 0)
      {
        continue;
      }

      var childCollection = ProcessInputParameter(inputParam, data, rootCollection.Name);
      if (childCollection != null)
      {
        rootCollection.Elements.Add(childCollection);
      }
    }

    dataAccess.SetData(0, new SpeckleCollectionWrapperGoo(rootCollection));
  }

  private SpeckleCollectionWrapper CreateRootCollection()
  {
    return new SpeckleCollectionWrapper
    {
      Base = new Collection(),
      Name = "Unnamed",
      Path = new List<string> { "Unnamed" },
      Color = null,
      Material = null,
      ApplicationId = InstanceGuid.ToString()
    };
  }

  private SpeckleCollectionWrapper? ProcessInputParameter(IGH_Param inputParam, List<IGH_Goo> data, string rootName)
  {
    var collections = data.OfType<SpeckleCollectionWrapperGoo>().Empty().ToList();
    var nonCollections = data.Where(t => t is not SpeckleCollectionWrapperGoo).Empty().ToList();

    // Validate input - cannot mix collections and objects
    if (collections.Count > 0 && nonCollections.Count > 0)
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        $"Parameter {inputParam.NickName} cannot contain both objects and collections."
      );
      return null;
    }

    var childPath = new List<string> { rootName, inputParam.NickName };
    var childCollection = new SpeckleCollectionWrapper
    {
      Base = new Collection(),
      Name = inputParam.NickName,
      Path = childPath,
      Color = null,
      Material = null,
      Topology = GrasshopperHelpers.GetParamTopology(inputParam),
      ApplicationId = inputParam.InstanceGuid.ToString()
    };

    if (collections.Count > 0)
    {
      ProcessCollectionInputs(collections, childCollection, childPath);
    }
    else
    {
      ProcessObjectInputs(nonCollections, childCollection, childPath);
    }

    return childCollection;
  }

  private void ProcessCollectionInputs(
    List<SpeckleCollectionWrapperGoo> collections,
    SpeckleCollectionWrapper parentCollection,
    List<string> childPath
  )
  {
    var duplicateNames = new HashSet<string>();

    foreach (var collectionGoo in collections.Select(c => (SpeckleCollectionWrapperGoo)c.Duplicate()))
    {
      collectionGoo.Value.Path = childPath;

      // Check for duplicate names within this collection
      foreach (
        var subCollectionName in collectionGoo.Value.Elements.OfType<SpeckleCollectionWrapper>().Select(c => c.Name)
      )
      {
        if (!duplicateNames.Add(subCollectionName))
        {
          AddRuntimeMessage(
            GH_RuntimeMessageLevel.Error,
            $"Duplicate collection name '{subCollectionName}' found. Collection names must be unique per level."
          );
          return;
        }
      }

      parentCollection.Elements.AddRange(collectionGoo.Value.Elements);
    }
  }

  private void ProcessObjectInputs(
    List<IGH_Goo> objects,
    SpeckleCollectionWrapper parentCollection,
    List<string> childPath
  )
  {
    foreach (var obj in objects)
    {
      // deep copy to avoid mutations
      if (obj?.ToSpeckleObjectWrapper() is SpeckleGeometryWrapper objWrapper)
      {
        SpeckleGeometryWrapper wrapper = objWrapper.DeepCopy();
        wrapper.Path = childPath;
        wrapper.Parent = parentCollection;
        parentCollection.Elements.Add(wrapper);
      }
      else
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{obj?.GetType().Name} type cannot be added to collections.");
      }
    }
  }

  // IGH_VariableParameterComponent implementation
  public override bool CanInsertParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input;

  public override bool CanRemoveParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input;

  public override bool DestroyParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input;

  public override IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    var param = CreateVariableParameter($"Sub-Collection {Params.Input.Count + 1}", GH_ParamAccess.tree);
    return param;
  }
}
