using System.Collections;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("C491D26C-84CB-4684-8BD2-AA78D0F2FE53")]
public class ExpandSpeckleObject : GH_Component, IGH_VariableParameterComponent
{
  public ExpandSpeckleObject()
    : base(
      "Expand Speckle Object",
      "ESO",
      "Expands a Speckle Object into its properties",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => BitmapBuilder.CreateCircleIconBitmap("eO");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleObjectParam(), "Object", "O", "Speckle Object to expand", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpeckleObjectWrapperGoo objectWrapperGoo = new();
    da.GetData(0, ref objectWrapperGoo);

    if (objectWrapperGoo is null)
    {
      return;
    }
    var o = objectWrapperGoo.Value;
    Name = o.Name ?? o.speckle_type;
    NickName = o.Name;

    List<OutputParamWrapper> outputParams = CreateOutputParamsFromSpeckleObject(objectWrapperGoo.Value.Base);
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
            da.SetData(i, outParamWrapper.Value);
            break;
          case GH_ParamAccess.list:
            da.SetDataList(i, outParamWrapper.Value as IList);
            break;
        }
      }
    }
  }

  private List<OutputParamWrapper> CreateOutputParamsFromSpeckleObject(Base @base)
  {
    List<OutputParamWrapper> result = new();

    if (@base == null)
    {
      return result;
    }

    foreach (
      var prop in @base.GetMembers(
        DynamicBaseMemberType.Instance | DynamicBaseMemberType.Dynamic | DynamicBaseMemberType.SchemaComputed
      )
    )
    {
      // Convert and add to corresponding output structure
      var value = prop.Value;
      switch (value)
      {
        case null:
          result.Add(CreateOutputParamByKeyValue(prop.Key, null, GH_ParamAccess.item));
          break;
        case IList list:
          List<object> nativeObjects = new();
          foreach (var x in list)
          {
            switch (x)
            {
              case Base xBase:
                List<GeometryBase> converted = SpeckleConversionContext.ConvertToHost(xBase);
                nativeObjects.AddRange(converted);
                break;
              default:
                nativeObjects.Add(x);
                break;
            }
          }
          result.Add(CreateOutputParamByKeyValue(prop.Key, nativeObjects, GH_ParamAccess.list));
          break;
        case Dictionary<string, object?> dict: // this should be treated a properties dict
          SpecklePropertyGroupGoo propertyGoo = new();
          propertyGoo.CastFrom(dict);
          result.Add(CreateOutputParamByKeyValue(prop.Key, propertyGoo, GH_ParamAccess.item));
          break;
        default:
          if (prop.Value is Base baseValue)
          {
            List<GeometryBase> converted = SpeckleConversionContext.ConvertToHost(baseValue);
            result.Add(CreateOutputParamByKeyValue(prop.Key, converted, GH_ParamAccess.list));
          }
          else
          {
            result.Add(CreateOutputParamByKeyValue(prop.Key, prop.Value, GH_ParamAccess.item));
          }
          break;
      }
    }

    return result;
  }

  private OutputParamWrapper CreateOutputParamByKeyValue(string key, object? value, GH_ParamAccess access)
  {
    Param_GenericObject param =
      new()
      {
        Name = key,
        NickName = key,
        Description = "",
        Access = access
      };

    return new OutputParamWrapper(param, value);
  }

  public bool CanInsertParameter(GH_ParameterSide side, int index) => false;

  public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;

  public void VariableParameterMaintenance() { }

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

  public bool DestroyParameter(GH_ParameterSide side, int index)
  {
    return side == GH_ParameterSide.Output;
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
}

public record OutputParamWrapper(Param_GenericObject Param, object? Value);
