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

  protected override void RegisterInputParams(GH_InputParamManager pManager) =>
    pManager.AddGenericParameter("Speckle Param", "SP", "Speckle param to deconstruct", GH_ParamAccess.item);

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    // on first iteration, discover all fields from all objects to create stable output structure
    if (da.Iteration == 0)
    {
      var allFields = DiscoverAllFieldsFromInput();

      if (allFields.Count > 0)
      {
        var requiredOutputs = CreateOutputParamsFromFieldNames(allFields);

        if (OutputMismatch(requiredOutputs))
        {
          OnPingDocument()?.ScheduleSolution(5, _ => CreateOutputs(requiredOutputs));
          return;
        }
      }
    }

    // process current object normally
    object data = new();
    if (!da.GetData(0, ref data))
    {
      return;
    }

    var outputParams = DeconstructObject(data);
    if (outputParams == null)
    {
      return;
    }

    // set component name based on the current object
    NickName = Name;

    // set output data - fill missing fields with nulls for objects that don't have all fields
    SetOutputData(da, outputParams);
  }

  /// <summary>
  /// Discovers all unique field names and their access types from all input objects by looking at volatile data directly.
  /// </summary>
  /// <returns>A dictionary mapping field names to their required parameter access types.</returns>
  private IReadOnlyDictionary<string, GH_ParamAccess> DiscoverAllFieldsFromInput()
  {
    Dictionary<string, GH_ParamAccess> allFields = [];

    foreach (var item in Params.Input[0].VolatileData.AllData(true))
    {
      var objectOutputs = DeconstructObject(item);
      if (objectOutputs != null)
      {
        foreach (var output in objectOutputs)
        {
          string fieldName = output.Param.Name;
          allFields[fieldName] = output.Param.Access;
        }
      }
    }

    return allFields;
  }

  /// <summary>
  /// Creates output parameter wrappers from field names and their corresponding access types.
  /// </summary>
  /// <param name="fieldAccessTypes">Dictionary mapping field names to their required parameter access types.</param>
  /// <returns>List of output parameter wrappers with correct access types.</returns>
  private List<OutputParamWrapper> CreateOutputParamsFromFieldNames(
    IReadOnlyDictionary<string, GH_ParamAccess> fieldAccessTypes
  ) => fieldAccessTypes.Select(kvp => CreateOutputParamByKeyValue(kvp.Key, null, kvp.Value)).ToList();

  /// <summary>
  /// Deconstructs a single object into its constituent fields/properties.
  /// </summary>
  private List<OutputParamWrapper>? DeconstructObject(object data) =>
    data switch
    {
      // get children elements from wrapper to override elements prop while parsing
      SpeckleCollectionWrapperGoo collectionGoo when collectionGoo.Value != null
        => ParseSpeckleWrapper(
          collectionGoo.Value,
          collectionGoo.Value.Elements.Select(o => ((SpeckleWrapper)o!).CreateGoo()).ToList()
        ),

      // get geometries from wrapper to override displayValue prop while parsing
      SpeckleDataObjectWrapperGoo dataObjectGoo when dataObjectGoo.Value != null
        => ParseSpeckleWrapper(
          dataObjectGoo.Value,
          null,
          dataObjectGoo.Value.Geometries.Select(o => o.CreateGoo()).ToList()
        ),

      SpeckleGeometryWrapperGoo objectGoo when objectGoo.Value != null => ParseSpeckleWrapper(objectGoo.Value),

      SpeckleBlockInstanceWrapperGoo blockInstanceGoo when blockInstanceGoo.Value != null
        => ParseSpeckleWrapper(blockInstanceGoo.Value),

      SpeckleBlockDefinitionWrapperGoo blockDef when blockDef.Value != null => ParseSpeckleWrapper(blockDef.Value),

      SpeckleMaterialWrapperGoo materialGoo when materialGoo.Value != null => ParseSpeckleWrapper(materialGoo.Value),

      SpecklePropertyGroupGoo propGoo when propGoo.Value != null => ParsePropertyGroup(propGoo),

      _ => HandleUnsupportedType(data),
    };

  /// <summary>
  /// Handles SpecklePropertyGroupGoo objects by extracting their key-value pairs.
  /// </summary>
  private List<OutputParamWrapper> ParsePropertyGroup(SpecklePropertyGroupGoo propGoo)
  {
    Name = $"properties ({propGoo.Value.Count})";
    List<OutputParamWrapper> objectOutputs = new();

    foreach (var key in propGoo.Value.Keys)
    {
      ISpecklePropertyGoo value = propGoo.Value[key];
      object? outputValue = value switch
      {
        SpecklePropertyGoo prop => prop.Value,
        SpecklePropertyGroupGoo propGroup => propGroup,
        _ => value,
      };

      // determine access type based on the value
      GH_ParamAccess access = outputValue is IList ? GH_ParamAccess.list : GH_ParamAccess.item;
      objectOutputs.Add(CreateOutputParamByKeyValue(key, outputValue, access));
    }

    return objectOutputs;
  }

  /// <summary>
  /// Handles unsupported object types by logging an error and returning null.
  /// </summary>
  private List<OutputParamWrapper>? HandleUnsupportedType(object data)
  {
    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Type cannot be deconstructed: {data.GetType().Name}");
    return null;
  }

  /// <summary>
  /// Sets output data for the current iteration, filling missing fields with null values.
  /// Uses a lookup dictionary for efficient field matching.
  /// </summary>
  private void SetOutputData(IGH_DataAccess da, List<OutputParamWrapper> currentOutputs)
  {
    if (Params.Output.Count == 0)
    {
      return;
    }

    // create a lookup for current outputs by field name
    var outputLookup = currentOutputs.ToDictionary(o => o.Param.Name, o => o.Value);

    // set data for each output parameter
    for (int i = 0; i < Params.Output.Count; i++)
    {
      var outputParam = Params.Output[i];

      // set the value if it exists, otherwise set null
      object? value = outputLookup.TryGetValue(outputParam.Name, out var fieldValue) ? fieldValue : null;

      switch (outputParam.Access)
      {
        case GH_ParamAccess.item:
          da.SetData(i, value);
          break;
        case GH_ParamAccess.list:
          da.SetDataList(i, value as IList ?? new List<object?>());
          break;
      }
    }
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

    // process each property of the Base object
    foreach (var prop in @base.GetMembers(DynamicBaseMemberType.Instance | DynamicBaseMemberType.Dynamic))
    {
      // skip internal dynamic property keys
      if (prop.Key == nameof(Base.DynamicPropertyKeys))
      {
        continue;
      }

      var outputParam = CreateOutputParamForProperty(prop, @base, elements, displayValue);
      if (outputParam != null)
      {
        result.Add(outputParam);
      }
    }

    return result;
  }

  /// <summary>
  /// Creates an output parameter for a single property, handling different value types appropriately.
  /// </summary>
  private OutputParamWrapper CreateOutputParamForProperty(
    KeyValuePair<string, object?> prop,
    Base @base,
    List<IGH_Goo>? elements,
    List<IGH_Goo>? displayValue
  ) =>
    prop.Value switch
    {
      null => CreateOutputParamByKeyValue(prop.Key, null, GH_ParamAccess.item),
      IList list => CreateListOutputParam(prop.Key, list, @base, elements, displayValue),
      Dictionary<string, object?> dict => CreateDictionaryOutputParam(prop.Key, dict),
      SpeckleWrapper wrapper => CreateOutputParamByKeyValue(prop.Key, wrapper.CreateGoo(), GH_ParamAccess.item),
      Base baseValue => CreateOutputParamByKeyValue(prop.Key, ConvertOrCreateWrapper(baseValue), GH_ParamAccess.list),
      _ => CreateOutputParamByKeyValue(prop.Key, prop.Value, GH_ParamAccess.item),
    };

  /// <summary>
  /// Creates an output parameter for list properties, with special handling for collection elements and display values.
  /// </summary>
  private OutputParamWrapper CreateListOutputParam(
    string key,
    IList list,
    Base @base,
    List<IGH_Goo>? elements,
    List<IGH_Goo>? displayValue
  )
  {
    // override list value for special cases
    IList actualList = key switch
    {
      "elements" when @base is Collection && elements != null => elements,
      "displayValue" when @base is Speckle.Objects.Data.DataObject && displayValue != null => displayValue,
      _ => list,
    };

    List<object> nativeObjects = new();
    foreach (var item in actualList)
    {
      switch (item)
      {
        case SpeckleWrapper wrapper:
          nativeObjects.Add(wrapper.CreateGoo());
          break;
        case Base baseItem:
          nativeObjects.AddRange(ConvertOrCreateWrapper(baseItem));
          break;
        default:
          nativeObjects.Add(item);
          break;
      }
    }

    return CreateOutputParamByKeyValue(key, nativeObjects, GH_ParamAccess.list);
  }

  /// <summary>
  /// Creates an output parameter for dictionary properties, converting them to SpecklePropertyGroupGoo.
  /// </summary>
  private OutputParamWrapper CreateDictionaryOutputParam(string key, Dictionary<string, object?> dict)
  {
    SpecklePropertyGroupGoo propertyGoo = new();
    propertyGoo.CastFrom(dict);
    return CreateOutputParamByKeyValue(key, propertyGoo, GH_ParamAccess.item);
  }

  /// <summary>
  /// Converts a Speckle Base object to host geometry or creates a wrapper if conversion fails.
  /// Returns a list of SpeckleGeometryWrapperGoo objects.
  /// </summary>
  private List<SpeckleGeometryWrapperGoo> ConvertOrCreateWrapper(Base @base)
  {
    try
    {
      // attempt conversion to host geometry
      List<(object, Base)> convertedBase = SpeckleConversionContext.Current.ConvertToHost(@base);
      return convertedBase.Select(CreateGeometryWrapper).ToList();
    }
    catch (ConversionException)
    {
      // fallback: create wrapper without conversion for objects that can't be converted
      return new List<SpeckleGeometryWrapperGoo> { CreateFallbackWrapper(@base) };
    }
  }

  /// <summary>
  /// Creates a SpeckleGeometryWrapperGoo from a converted geometry and base object pair.
  /// </summary>
  private SpeckleGeometryWrapperGoo CreateGeometryWrapper((object geometry, Base @base) converted)
  {
    SpeckleGeometryWrapper wrapper =
      new()
      {
        Base = converted.@base,
        GeometryBase = converted.geometry as GeometryBase,
        Name = converted.@base["name"] as string ?? "",
        Color = null,
        Material = null,
      };
    return new SpeckleGeometryWrapperGoo(wrapper);
  }

  /// <summary>
  /// Creates a fallback wrapper for Base objects that cannot be converted to host geometry.
  /// </summary>
  private SpeckleGeometryWrapperGoo CreateFallbackWrapper(Base @base)
  {
    SpeckleGeometryWrapper wrapper =
      new()
      {
        Base = @base,
        GeometryBase = null,
        Name = @base[Constants.NAME_PROP] as string ?? "",
        Color = null,
        Material = null,
      };
    return new SpeckleGeometryWrapperGoo(wrapper);
  }

  private OutputParamWrapper CreateOutputParamByKeyValue(string key, object? value, GH_ParamAccess access)
  {
    SpeckleOutputParam param =
      new()
      {
        Name = key,
        NickName = key,
        Description = "",
        Access = access,
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
      Optional = true,
    };
    myParam.NickName = myParam.Name;
    return myParam;
  }

  public bool DestroyParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Output;

  private void CreateOutputs(List<OutputParamWrapper> outputParams)
  {
    // remove all existing output parameters
    while (Params.Output.Count > 0)
    {
      Params.UnregisterOutputParameter(Params.Output[^1]);
    }

    // add new output parameters
    foreach (var newParam in outputParams)
    {
      var param = new SpeckleOutputParam
      {
        Name = newParam.Param.Name,
        NickName = newParam.Param.NickName,
        MutableNickName = false,
        Access = newParam.Param.Access,
      };
      Params.RegisterOutputParam(param);
    }

    // notify Grasshopper of parameter changes
    Params.OnParametersChanged();
    VariableParameterMaintenance();
    ExpireSolution(false);
  }

  /// <summary>
  /// Determines if the current output parameter structure differs from the required structure.
  /// </summary>
  private bool OutputMismatch(List<OutputParamWrapper> outputParams)
  {
    if (Params.Output.Count != outputParams.Count)
    {
      return true;
    }

    for (int i = 0; i < outputParams.Count; i++)
    {
      var newParam = outputParams[i];
      var oldParam = Params.Output[i];
      if (
        oldParam.NickName != newParam.Param.NickName
        || oldParam.Name != newParam.Param.Name
        || oldParam.Access != newParam.Param.Access
      )
      {
        return true;
      }
    }

    return false;
  }
}

public record OutputParamWrapper(Param_GenericObject Param, object? Value);
