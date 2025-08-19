using System.Collections;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

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
    pManager.AddGenericParameter("Speckle Param", "SP", "Speckle param(s) to deconstruct", GH_ParamAccess.list);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  /// <summary>
  /// Processes multiple objects and creates unified output parameters containing all unique fields from all input objects.
  /// </summary>
  protected override void SolveInstance(IGH_DataAccess da)
  {
    List<object> inputData = new();
    if (!da.GetDataList(0, inputData) || inputData.Count == 0)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No objects provided to deconstruct.");
      return;
    }

    // collect all unique field names from all objects
    HashSet<string> allFieldNames = new();
    List<List<OutputParamWrapper>> allObjectOutputs = new();

    // process each input object to collect its fields
    foreach (object data in inputData)
    {
      List<OutputParamWrapper>? objectOutputs = DeconstructObject(data);
      if (objectOutputs == null)
      {
        return;
      }

      allObjectOutputs.Add(objectOutputs);
      foreach (var output in objectOutputs)
      {
        allFieldNames.Add(output.Param.Name);
      }
    }

    // create unified output parameters from all unique fields
    List<OutputParamWrapper> finalOutputParams = CreateUnifiedOutputs(allFieldNames, allObjectOutputs);

    // update component name depending on input
    Name = inputData.Count == 1 ? Name : $"Multiple Objects ({inputData.Count})";
    NickName = Name;

    if (OutputMismatch(finalOutputParams))
    {
      OnPingDocument()
        .ScheduleSolution(
          5,
          _ =>
          {
            CreateOutputs(finalOutputParams);
          }
        );
    }
    else
    {
      for (int i = 0; i < finalOutputParams.Count; i++)
      {
        var outParamWrapper = finalOutputParams[i];
        if (outParamWrapper.Value is IList list)
        {
          da.SetDataList(i, list);
        }
        else
        {
          da.SetDataList(i, new List<object?> { outParamWrapper.Value });
        }
      }
    }
  }

  /// <summary>
  /// Deconstructs a single object into its constituent fields/properties.
  /// </summary>
  private List<OutputParamWrapper>? DeconstructObject(object data)
  {
    switch (data)
    {
      case SpeckleCollectionWrapperGoo collectionGoo when collectionGoo.Value != null:
        // get children elements from the wrapper to override the elements prop while parsing
        var children = collectionGoo.Value.Elements.Select(o => ((SpeckleWrapper)o).CreateGoo()).ToList();
        return ParseSpeckleWrapper(collectionGoo.Value, children);

      case SpeckleDataObjectWrapperGoo dataObjectGoo when dataObjectGoo.Value != null:
        // get geometries from the wrapper to override the displayvalue prop while parsing
        var display = dataObjectGoo.Value.Geometries.Select(o => o.CreateGoo()).ToList();
        return ParseSpeckleWrapper(dataObjectGoo.Value, null, display);

      case SpeckleGeometryWrapperGoo objectGoo when objectGoo.Value != null:
        return ParseSpeckleWrapper(objectGoo.Value);

      case SpeckleBlockInstanceWrapperGoo blockInstanceGoo when blockInstanceGoo.Value != null:
        return ParseSpeckleWrapper(blockInstanceGoo.Value);

      case SpeckleBlockDefinitionWrapperGoo blockDef:
        return ParseSpeckleWrapper(blockDef.Value);

      case SpeckleMaterialWrapperGoo materialGoo when materialGoo.Value != null:
        return ParseSpeckleWrapper(materialGoo.Value);

      case SpecklePropertyGroupGoo propGoo:
        Name = $"properties ({propGoo.Value.Count})";
        List<OutputParamWrapper> objectOutputs = new();
        foreach (var key in propGoo.Value.Keys)
        {
          ISpecklePropertyGoo value = propGoo.Value[key];
          object? outputValue = value is SpecklePropertyGoo prop
            ? prop.Value
            : value is SpecklePropertyGroupGoo propGroup
              ? propGroup
              : value;
          objectOutputs.Add(CreateOutputParamByKeyValue(key, outputValue, GH_ParamAccess.item));
        }
        return objectOutputs;

      default:
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Type cannot be deconstructed: {data.GetType().Name}");
        return null;
    }
  }

  /// <summary>
  /// Creates unified output parameters by collecting all unique field names from all input objects and creating
  /// list-based outputs where missing fields are represented as null values.
  /// </summary>
  private List<OutputParamWrapper> CreateUnifiedOutputs(
    HashSet<string> allFieldNames,
    List<List<OutputParamWrapper>> allObjectOutputs
  )
  {
    List<OutputParamWrapper> finalOutputParams = new();

    foreach (string fieldName in allFieldNames.OrderBy(x => x))
    {
      List<object?> fieldValues = new();

      foreach (var objectOutputs in allObjectOutputs)
      {
        var fieldOutput = objectOutputs.FirstOrDefault(o => o.Param.Name == fieldName);

        if (fieldOutput?.Value is IList existingList && fieldOutput.Param.Access == GH_ParamAccess.list)
        {
          fieldValues.Add(existingList);
        }
        else
        {
          fieldValues.Add(fieldOutput?.Value);
        }
      }

      finalOutputParams.Add(CreateOutputParamByKeyValue(fieldName, fieldValues, GH_ParamAccess.list));
    }

    return finalOutputParams;
  }

  private List<OutputParamWrapper> ParseSpeckleWrapper(
    SpeckleWrapper wrapper,
    List<IGH_Goo>? elements = null,
    List<IGH_Goo>? displayValue = null
  )
  {
    Name = string.IsNullOrEmpty(wrapper.Name) ? wrapper.Base.speckle_type : wrapper.Name;
    return CreateOutputParamsFromBase(wrapper.Base, elements, displayValue);
  }

  private List<OutputParamWrapper> CreateOutputParamsFromBase(
    Base @base,
    List<IGH_Goo>? elements = null,
    List<IGH_Goo>? displayValue = null
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

          // override list value if base is a collection and this is the elements prop, since this is empty if coming from a collectionwrapper
          if (@base is Collection && prop.Key == "elements" && elements != null)
          {
            list = elements;
          }

          // override list value if base is a dataobject and this is the displayvalue prop, since this is empty if coming from a dataobject wrapper
          if (@base is Speckle.Objects.Data.DataObject && prop.Key == "displayValue" && displayValue != null)
          {
            list = displayValue;
          }

          foreach (var x in list)
          {
            switch (x)
            {
              case SpeckleWrapper wrapper:
                nativeObjects.Add(wrapper.CreateGoo());
                break;

              case Base xBase:
                nativeObjects.AddRange(ConvertOrCreateWrapper(xBase));
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

        case SpeckleWrapper wrapper:
          result.Add(CreateOutputParamByKeyValue(prop.Key, wrapper.CreateGoo(), GH_ParamAccess.item));
          break;

        case Base baseValue:
          result.Add(CreateOutputParamByKeyValue(prop.Key, ConvertOrCreateWrapper(baseValue), GH_ParamAccess.list));
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

    return result;
  }

  private List<SpeckleGeometryWrapperGoo> ConvertOrCreateWrapper(Base @base)
  {
    try
    {
      // convert the base and create a wrapper for each result
      List<(object, Base)> convertedBase = SpeckleConversionContext.Current.ConvertToHost(@base);
      List<SpeckleGeometryWrapperGoo> convertedWrappers = new();
      foreach ((object o, Base b) in convertedBase)
      {
        GeometryBase? g = o as GeometryBase;
        SpeckleGeometryWrapper convertedWrapper =
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

      return convertedWrappers;
    }
    catch (ConversionException)
    {
      // some classes, like RawEncoding, have no direct conversion or fallback value.
      // when this is the case, wrap it to allow users to further expand the object.
      SpeckleGeometryWrapper convertedWrapper =
        new()
        {
          Base = @base,
          GeometryBase = null,
          Name = @base[Constants.NAME_PROP] as string ?? "",
          Color = null,
          Material = null
        };

      return new() { new SpeckleGeometryWrapperGoo(convertedWrapper) };
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
