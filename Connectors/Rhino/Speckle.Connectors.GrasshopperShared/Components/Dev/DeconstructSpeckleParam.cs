using System.Collections;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Dev;

[Guid("C491D26C-84CB-4684-8BD2-AA78D0F2FE53")]
public class DeconstructSpeckleParam : GH_Component, IGH_VariableParameterComponent
{
  public DeconstructSpeckleParam()
    : base(
      "Deconstruct",
      "D",
      "Deconstructs any Speckle param into its atomic parts",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_deconstruct;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddGenericParameter(
      "Speckle Param",
      "SP",
      "Speckle param to deconstruct. Expects Collections, Objects, or Materials",
      GH_ParamAccess.item
    );
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    object data = new();
    da.GetData(0, ref data);

    List<OutputParamWrapper> outputParams = new();

    switch (data)
    {
      case SpeckleObjectWrapperGoo obj:
        Name = string.IsNullOrEmpty(obj.Value.Name) ? obj.Value.Base.speckle_type : obj.Value.Name;
        outputParams = CreateOutputParamsFromBase(obj.Value.Base, obj.Value.Color, obj.Value.Material);
        break;
      case SpeckleCollectionWrapperGoo coll:
        Name = string.IsNullOrEmpty(coll.Value.Collection.name)
          ? coll.Value.Collection.speckle_type
          : coll.Value.Collection.name;
        outputParams = CreateOutputParamsFromBase(coll.Value.Collection, coll.Value.Color, coll.Value.Material);
        break;
      case SpeckleMaterialWrapperGoo matGoo:
        Name = string.IsNullOrEmpty(matGoo.Value.Base.name) ? matGoo.Value.Base.speckle_type : matGoo.Value.Base.name;
        outputParams = CreateOutputParamsFromBase(matGoo.Value.Base);
        break;
      default:
        return;
    }

    NickName = Name;

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

  private List<OutputParamWrapper> CreateOutputParamsFromBase(
    Base @base,
    Color? color = null,
    SpeckleMaterialWrapper? materialWrapper = null
  )
  {
    List<OutputParamWrapper> result = new();
    if (@base == null)
    {
      return result;
    }

    // cycle through base props
    foreach (var prop in @base.GetMembers(DynamicBaseMemberType.Instance | DynamicBaseMemberType.Dynamic))
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
              case SpeckleCollectionWrapper collWrapper:
                nativeObjects.Add(new SpeckleCollectionWrapperGoo(collWrapper));
                break;

              case SpeckleObjectWrapper objWrapper:
                nativeObjects.Add(new SpeckleObjectWrapperGoo(objWrapper));
                break;

              case Base xBase:
                nativeObjects.Add(ConvertOrCreateWrapper(prop.Key, xBase));
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

        case SpeckleCollectionWrapper collWrapper:
          result.Add(
            CreateOutputParamByKeyValue(prop.Key, new SpeckleCollectionWrapperGoo(collWrapper), GH_ParamAccess.item)
          );
          break;

        case SpeckleObjectWrapper objWrapper:
          result.Add(
            CreateOutputParamByKeyValue(prop.Key, new SpeckleObjectWrapperGoo(objWrapper), GH_ParamAccess.item)
          );
          break;

        case Base baseValue:
          result.Add(ConvertOrCreateWrapper(prop.Key, baseValue));
          break;

        default:
          // we don't want to output dynamic property keys
          if (prop.Key == nameof(Base.DynamicPropertyKeys))
          {
            continue;
          }

          result.Add(CreateOutputParamByKeyValue(prop.Key, prop.Value, GH_ParamAccess.item));
          break;
      }
    }

    // add color and render material
    if (color is not null)
    {
      result.Add(CreateOutputParamByKeyValue("color", color, GH_ParamAccess.item));
    }

    if (materialWrapper is not null)
    {
      SpeckleMaterialWrapperGoo materialWrapperGoo = new(materialWrapper);
      result.Add(CreateOutputParamByKeyValue("renderMaterial", materialWrapperGoo, GH_ParamAccess.item));
    }

    return result;
  }

  private OutputParamWrapper ConvertOrCreateWrapper(string propKey, Base @base)
  {
    try
    {
      // convert the base and create a wrapper for each result
      List<(GeometryBase, Base)> convertedBase = SpeckleConversionContext.ConvertToHost(@base);
      List<SpeckleObjectWrapperGoo> convertedWrappers = new();
      foreach ((GeometryBase g, Base b) in convertedBase)
      {
        SpeckleObjectWrapper convertedWrapper =
          new()
          {
            Base = b,
            GeometryBase = g,
            Name = b["name"] as string ?? "",
            Color = null,
            Material = null
          };
        convertedWrappers.Add(new(convertedWrapper));
      }
      return CreateOutputParamByKeyValue(propKey, convertedWrappers, GH_ParamAccess.list);
    }
    catch (ConversionException)
    {
      // some classes, like RawEncoding, have no direct conversion or fallback value.
      // when this is the case, wrap it to allow users to further expand the object.
      SpeckleObjectWrapper convertedWrapper =
        new()
        {
          Base = @base,
          GeometryBase = null,
          Name = @base["name"] as string ?? "",
          Color = null,
          Material = null
        };
      return CreateOutputParamByKeyValue(propKey, new SpeckleObjectWrapperGoo(convertedWrapper), GH_ParamAccess.item);
    }
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
