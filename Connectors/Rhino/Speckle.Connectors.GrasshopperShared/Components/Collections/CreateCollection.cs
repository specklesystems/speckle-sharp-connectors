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
    if (HasDuplicateApplicationIds(rootCollection))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The same object(s) cannot appear in multiple collections");
      return;
    }

    // validate collection isn't empty (CNX-2855)
    if (rootCollection.Elements.Count == 0 || !rootCollection.Elements.Any(HasAnyValidContent))
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        "Collection contains no valid geometry. All input objects are unsupported types."
      );
      return;
    }

    dataAccess.SetData(0, new SpeckleCollectionWrapperGoo(rootCollection));
  }

  /// <summary>
  /// Recursively checks if collection or any descendants contain valid geometry/data objects
  /// </summary>
  private bool HasAnyValidContent(ISpeckleCollectionObject? element) =>
    element switch
    {
      SpeckleGeometryWrapper => true,
      SpeckleDataObjectWrapper => true,
      SpeckleCollectionWrapper collection => collection.Elements.Any(HasAnyValidContent),
      _ => false
    };

  private SpeckleCollectionWrapper CreateRootCollection() =>
    new()
    {
      Base = new Collection(),
      Name = "Unnamed",
      Path = new List<string> { "Unnamed" },
      Color = null,
      Material = null,
      ApplicationId = InstanceGuid.ToString()
    };

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
        var subCollectionName in collectionGoo
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
    int skippedCount = 0;

    foreach (var obj in objects)
    {
      // handle data objects directly (deep copy to avoid mutations)
      // NOTE: DataObject first, since a DataObject with one geo is castable to speckle geometry
      if (obj is SpeckleDataObjectWrapperGoo dataObjectWrapperGoo)
      {
        var dataObjectWrapper = dataObjectWrapperGoo.Value.DeepCopy();
        dataObjectWrapper.Path = childPath;
        dataObjectWrapper.Parent = parentCollection;
        parentCollection.Elements.Add(dataObjectWrapper);
      }
      // handle geometry objects (deep copy to avoid mutations)
      else if (obj?.ToSpeckleGeometryWrapper() is SpeckleGeometryWrapper objWrapper)
      {
        SpeckleGeometryWrapper wrapper = objWrapper.DeepCopy();
        wrapper.Path = childPath;
        wrapper.Parent = parentCollection;
        parentCollection.Elements.Add(wrapper);
      }
      else
      {
        // add null placeholder to preserve topology (CNX-2855)
        parentCollection.Elements.Add(null);
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
  }

  /// <summary>
  /// Validates that all application IDs are unique across the entire collection hierarchy.
  /// Shows an error if duplicates are found, indicating objects appear in multiple collections.
  /// </summary>
  /// <returns>True if duplicates exist, false if all IDs are unique</returns>
  private bool HasDuplicateApplicationIds(SpeckleCollectionWrapper rootCollection)
  {
    // args to CheckForDuplicateApplicationIds passed in since the method can recursively check
    var seenIds = new HashSet<string>();
    var duplicateIds = new HashSet<string>();

    // iterate, create hash set and check all application IDs
    ProcessAndCheckForDuplicateApplicationIds(rootCollection, seenIds, duplicateIds);

    return duplicateIds.Count > 0;
  }

  /// <summary>
  /// Recursively collects application IDs from all in the collection hierarchy.
  /// </summary>
  /// <remarks>
  /// Only checks the wrapper's ApplicationId, not for example geometries within DataObjects.
  /// </remarks>
  private void ProcessAndCheckForDuplicateApplicationIds(
    SpeckleCollectionWrapper collection,
    HashSet<string> seenIds,
    HashSet<string> duplicateIds
  )
  {
    foreach (var element in collection.Elements)
    {
      switch (element)
      {
        case null:
          break; // skip nulls (CNX-2855)
        case SpeckleCollectionWrapper childCollection:
          // recurse into child collections
          ProcessAndCheckForDuplicateApplicationIds(childCollection, seenIds, duplicateIds);
          break;

        case SpeckleWrapper wrapper:
          if (wrapper.ApplicationId != null && !seenIds.Add(wrapper.ApplicationId))
          {
            duplicateIds.Add(wrapper.ApplicationId);
          }
          break;
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
