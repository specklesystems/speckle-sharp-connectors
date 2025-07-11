using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("116F08A5-BAA7-45B3-B6C8-469E452C9AC7")]
public class GetObjectProperties : GH_Component, IGH_VariableParameterComponent
{
  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => Resources.speckle_properties_query;
  public override GH_Exposure Exposure => GH_Exposure.quarternary;

  public GetObjectProperties()
    : base(
      "Query Properties",
      "qP",
      "Retrieves the values of the properties inside Speckle Objects at the specified keys",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddGenericParameter(
      "Objects",
      "O",
      "Speckle Objects to retrieve properties. Speckle Geometry and Data Objects are accepted.",
      GH_ParamAccess.item
    );
    pManager.AddTextParameter("Keys", "K", "Property keys to filter by", GH_ParamAccess.list);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    List<string> paths = new();
    da.GetDataList(1, paths);

    if (paths.Count == 0)
    {
      return;
    }

    if (OutputMismatch(paths))
    {
      OnPingDocument()
        .ScheduleSolution(
          5,
          _ =>
          {
            CreateOutputs(paths);
          }
        );
    }
    else
    {
      IGH_Goo? inputObject = null;
      SpecklePropertyGroupGoo? properties = null;

      if (da.GetData(0, ref inputObject))
      {
        // Handle DataObjects directly first
        if (inputObject is SpeckleDataObjectWrapperGoo dataObjectGoo)
        {
          properties = dataObjectGoo.Value.Properties;
        }
        // Handle both Geometry and DataObject wrappers using extension method
        else if (inputObject?.ToSpeckleGeometryWrapper() is SpeckleGeometryWrapper geoWrapper)
        {
          properties = geoWrapper.Properties;
        }
        else
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Unsupported object type: {inputObject?.TypeName}");
          return;
        }
      }

      if (properties == null || properties.Value.Count == 0)
      {
        return;
      }

      Dictionary<string, SpecklePropertyGoo> flattenedProps = properties.Flatten();

      for (int i = 0; i < paths.Count; i++)
      {
        var name = paths[i];
        if (FindProperty(flattenedProps, name) is SpecklePropertyGoo prop)
        {
          da.SetData(i, prop.Value);
        }
        else
        {
          da.SetData(i, null);
        }
      }
    }
  }

  // attempts to find a property by concatenated key, or returns null if not
  private SpecklePropertyGoo? FindProperty(Dictionary<string, SpecklePropertyGoo> props, string unifiedPath)
  {
    if (!props.TryGetValue(unifiedPath, out SpecklePropertyGoo currentGoo))
    {
      return null;
    }

    return currentGoo;
  }

  private bool OutputMismatch(List<string> outputParams)
  {
    if (Params.Output.Count != outputParams.Count)
    {
      return true;
    }

    var count = 0;
    foreach (var newParam in outputParams)
    {
      var oldParam = Params.Output[count];
      if (oldParam.NickName != newParam || oldParam.Name != newParam)
      {
        return true;
      }
      count++;
    }

    return false;
  }

  private void CreateOutputs(List<string> outputParams)
  {
    // Ensure we have the required count of output parameters
    while (Params.Output.Count != outputParams.Count)
    {
      if (Params.Output.Count > outputParams.Count) // if too many, unregister
      {
        Params.UnregisterOutputParameter(Params.Output[^1]);
      }

      if (Params.Output.Count < outputParams.Count) // if too little, add some
      {
        var param = new Param_GenericObject
        {
          Name = "newParam",
          NickName = "newParam",
          MutableNickName = false,
          Access = GH_ParamAccess.item
        };
        Params.RegisterOutputParam(param);
      }
    }

    // now unify names and nicknames
    int index = 0;
    foreach (var newParam in outputParams)
    {
      Params.Output[index].NickName = newParam;
      Params.Output[index].Name = newParam;
      index++;
    }

    // now we can update the output params
    Params.OnParametersChanged();
    VariableParameterMaintenance();
    ExpireSolution(false);
  }

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

  public void VariableParameterMaintenance() { }
}
