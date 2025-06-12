using System.Collections;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.GrasshopperShared.Components.Collections;

#pragma warning disable CA1711
[Guid("69BC8CFB-A72F-4A83-9263-F3399DDA2E5E")]
public class ExpandCollection : GH_Component, IGH_VariableParameterComponent
#pragma warning restore CA1711
{
  public ExpandCollection()
    : base(
      "Expand Collection",
      "eC",
      "Expands a Collection into its children",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.COLLECTIONS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_collections_expand;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "Data",
      "D",
      "The data you want to expand",
      GH_ParamAccess.item
    );
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpeckleCollectionWrapperGoo res = new();
    da.GetData(0, ref res);
    SpeckleCollectionWrapper wrapper = res.Value;
    if (wrapper is null)
    {
      return;
    }
    Name = wrapper.Name;
    NickName = wrapper.Name;

    // Separate objects and collections
    // Note: SpeckleBlockInstanceWrapper inherits from SpeckleObjectWrapper,
    // so it will be included in objects
    var objects = wrapper.Elements.OfType<SpeckleObjectWrapper>().ToList();
    var collections = wrapper.Elements.OfType<SpeckleCollectionWrapper>().ToList();

    var outputParams = new List<OutputParamWrapper>();
    if (objects.Count != 0)
    {
      var param = new Param_GenericObject()
      {
        Name = "_objects",
        NickName = "_objs",
        Description =
          "Some collections may contain a mix of objects and other collections. Here we output the atomic objects from within this collection.",
        Access = GH_ParamAccess.list
      };

      // Create appropriate Goo types for each object (downside of the inheritance refactor)
      List<IGH_Goo> atomicObjectGoos = new();

      foreach (var obj in objects)
      {
        if (obj is SpeckleBlockInstanceWrapper instanceWrapper)
        {
          atomicObjectGoos.Add(new SpeckleBlockInstanceWrapperGoo(instanceWrapper));
        }
        else if (obj is SpeckleObjectWrapper objWrapper)
        {
          atomicObjectGoos.Add(new SpeckleObjectWrapperGoo(objWrapper));
        }
      }

      outputParams.Add(new OutputParamWrapper(param, atomicObjectGoos, null));
    }

    foreach (SpeckleCollectionWrapper childWrapper in collections)
    {
      /* POC: we shouldn't skip empty, people would probably expect to see what they see in browser.
      if (childWrapper.Elements.Count == 0)
      {
        continue;
      }
      */

      var hasInnerCollections = childWrapper.Elements.Any(el => el is SpeckleCollectionWrapper);
      var topology = childWrapper.Topology;
      var nickName = childWrapper.Name;
      if (nickName.Length > 16)
      {
        nickName = childWrapper.Name[..6];
        nickName += "..." + childWrapper.Name[^6..];
      }

      var param = new Param_GenericObject()
      {
        Name = childWrapper.Name,
        NickName = nickName,
        Access = hasInnerCollections
          ? GH_ParamAccess.item
          : topology is null
            ? GH_ParamAccess.list
            : GH_ParamAccess.tree
      };

      object outputValue;
      if (hasInnerCollections)
      {
        outputValue = new SpeckleCollectionWrapperGoo(childWrapper);
      }
      else
      {
        // Create appropriate Goo types for child objects
        // feels like we're working around a design decision here
        List<IGH_Goo> childObjectGoos = new();
        foreach (var obj in childWrapper.Elements.OfType<SpeckleObjectWrapper>())
        {
          if (obj is SpeckleBlockInstanceWrapper instanceWrapper)
          {
            childObjectGoos.Add(new SpeckleBlockInstanceWrapperGoo(instanceWrapper));
          }
          else
          {
            childObjectGoos.Add(new SpeckleObjectWrapperGoo(obj));
          }
        }
        outputValue = childObjectGoos;
      }

      outputParams.Add(new OutputParamWrapper(param, outputValue, topology));
    }

    if (da.Iteration == 0 && OutputMismatch(outputParams))
    {
      OnPingDocument()
        .ScheduleSolution(
          5,
          _ =>
          {
            CreateOutputs(outputParams);
          }
        );
    }
    else
    {
      for (int i = 0; i < outputParams.Count; i++)
      {
        var outParam = Params.Output[i];
        var outParamWrapper = outputParams[i];
        switch (outParam.Access)
        {
          case GH_ParamAccess.item:
            da.SetData(i, outParamWrapper.Values);
            break;
          case GH_ParamAccess.list:
            da.SetDataList(i, outParamWrapper.Values as IList);
            break;
          case GH_ParamAccess.tree:
            var topo = outParamWrapper.Topology.NotNull();
            var values = outParamWrapper.Values as IList;
            var tree = GrasshopperHelpers.CreateDataTreeFromTopologyAndItems(topo, values.NotNull());
            da.SetDataTree(i, tree);
            break;
        }
      }
    }
  }

  private bool OutputMismatch(List<OutputParamWrapper> outputParams)
  {
    if (Params.Output.Count != outputParams.Count)
    {
      return true;
    }

    var count = 0;
    foreach (var newParam in outputParams)
    {
      var oldParam = Params.Output[count];
      if (
        oldParam.NickName != newParam.Param.NickName
        || oldParam.Name != newParam.Param.Name
        || oldParam.Access != newParam.Param.Access
      )
      {
        return true;
      }
      count++;
    }

    return false;
  }

  private void CreateOutputs(List<OutputParamWrapper> outputParams)
  {
    // TODO: better, nicer handling of creation/removal
    while (Params.Output.Count > 0)
    {
      Params.UnregisterOutputParameter(Params.Output[^1]);
    }

    foreach (var newParam in outputParams)
    {
      var param = new Param_GenericObject
      {
        Name = newParam.Param.Name,
        NickName = newParam.Param.NickName,
        MutableNickName = false,
        Access = newParam.Param.Access
      };
      Params.RegisterOutputParam(param);
    }

    Params.OnParametersChanged();
    VariableParameterMaintenance();
    ExpireSolution(false);
  }

  public void VariableParameterMaintenance() { }

  public bool CanInsertParameter(GH_ParameterSide side, int index) => false;

  public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;

  public IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    var myParam = new Param_GenericObject
    {
      Name = GH_ComponentParamServer.InventUniqueNickname("ABCD", Params.Input),
      MutableNickName = true,
      Optional = true
    };
    myParam.NickName = myParam.Name;
    return myParam;
  }

  public bool DestroyParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Output;
}

public record OutputParamWrapper(Param_GenericObject Param, object Values, string? Topology);
