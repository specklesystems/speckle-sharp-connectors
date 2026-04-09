using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
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
    var rootCollection = CollectionHelpers.CreateRootCollection(InstanceGuid.ToString());
    bool hasAnyInput = false;

    foreach (var inputParam in Params.Input)
    {
      var data = inputParam.VolatileData.AllData(true).ToList();
      if (data.Count == 0)
      {
        continue;
      }

      hasAnyInput = true;
      var childCollection = ProcessInputParameter(inputParam, data, rootCollection.Name);
      if (childCollection != null)
      {
        rootCollection.Elements.Add(childCollection);
      }
    }

    // Skip validation if no input provided
    if (!hasAnyInput)
    {
      return;
    }

    // validate for duplicate application IDs across the entire collection hierarchy
    if (CollectionHelpers.HasDuplicateApplicationIds(rootCollection))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The same object(s) cannot appear in multiple collections");
      return;
    }

    // validate collection isn't empty (CNX-2855)
    if (rootCollection.Elements.Count == 0 || !rootCollection.Elements.Any(CollectionHelpers.HasAnyValidContent))
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        "Collection contains no valid geometry. All input objects are unsupported types."
      );
      return;
    }

    dataAccess.SetData(0, new SpeckleCollectionWrapperGoo(rootCollection));
  }

  private SpeckleCollectionWrapper? ProcessInputParameter(IGH_Param inputParam, List<IGH_Goo> data, string rootName)
  {
    var childPath = new List<string> { rootName, inputParam.NickName };
    var childCollection = new SpeckleCollectionWrapper
    {
      Base = new Collection(),
      Name = inputParam.NickName,
      Path = childPath,
      Color = null,
      Material = null,
      Topology = GrasshopperHelpers.GetParamTopology(inputParam),
      ApplicationId = inputParam.InstanceGuid.ToString(),
    };

    var duplicateNames = new HashSet<string>();
    int skippedCount = 0;

    foreach (var obj in data)
    {
      if (obj is SpeckleCollectionWrapperGoo collectionGoo)
      {
        var colClone = (SpeckleCollectionWrapperGoo)collectionGoo.Duplicate();
        colClone.Value.Path = childPath;

        // Check for duplicate names within this collection
        foreach (
          var subCollectionName in colClone
            .Value.Elements.Where(e => e != null) // skip nulls (CNX-2855)
            .OfType<SpeckleCollectionWrapper>()
            .Select(c => c.Name)
        )
        {
          if (!duplicateNames.Add(subCollectionName))
          {
            AddRuntimeMessage(
              GH_RuntimeMessageLevel.Error,
              $"Duplicate collection name '{subCollectionName}' found. Collection names must be unique per level."
            );
            return null;
          }
        }

        childCollection.Elements.AddRange(colClone.Value.Elements);
      }
      // handle data objects directly (deep copy to avoid mutations)
      // NOTE: DataObject first, since a DataObject with one geo is castable to speckle geometry
      else if (obj is SpeckleDataObjectWrapperGoo dataObjectWrapperGoo)
      {
        var dataObjectWrapper = dataObjectWrapperGoo.Value.DeepCopy();
        dataObjectWrapper.Path = childPath;
        dataObjectWrapper.Parent = childCollection;
        childCollection.Elements.Add(dataObjectWrapper);
      }
      // handle geometry objects (deep copy to avoid mutations)
      else if (obj?.ToSpeckleGeometryWrapper() is SpeckleGeometryWrapper objWrapper)
      {
        SpeckleGeometryWrapper wrapper = objWrapper.DeepCopy();
        wrapper.Path = childPath;
        wrapper.Parent = childCollection;
        childCollection.Elements.Add(wrapper);
      }
      else
      {
        // add null placeholder to preserve topology (CNX-2855)
        childCollection.Elements.Add(null);
        skippedCount++;
      }
    }

    // add warning if objects were skipped (CNX-2855)
    if (skippedCount > 0)
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Warning,
        $"Skipped {skippedCount} unsupported object(s) (Leaders, TextDots, Dimensions, etc.)"
      );
    }

    return childCollection;
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
