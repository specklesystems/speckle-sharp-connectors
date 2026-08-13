using GH_IO.Serialization;
using Grasshopper.Kernel;
using Speckle.Sdk;

namespace Speckle.Connectors.GrasshopperShared.Components;

// NOTE: The number of spaces determines the order in which they display in the ribbon (nice hack)
public static class ComponentCategories
{
  // ribbon
  public const string PRIMARY_RIBBON = "Speckle";

  // categories
  public const string OPERATIONS = "    Models";
  public const string OBJECTS = "  Objects";
  public const string COLLECTIONS = "   Collections";
  public const string PARAMETERS = " Params";
  public const string DEVELOPER = "Dev";
}

public enum ComponentState
{
  Expired,
  NeedsInput,
  Receiving,
  Ready,
  Sending,
  UpToDate,
}

internal static class LegacyParamRepair
{
  /// <summary>
  /// Reads a component that has gained <see cref="IGH_VariableParameterComponent"/> since files referencing it were
  /// saved, restoring the fixed parameters that Grasshopper discards for such files.
  /// </summary>
  /// <remarks>
  /// GH_ComponentParamServer.ReadAllParameterData clears the registered parameters before looking for the
  /// "ParameterData" chunk that only variable parameter components write, so for a file saved while this component was
  /// still fixed it finds nothing to rebuild from and leaves the component with no parameters at all.
  /// </remarks>
  public static bool ReadRepairingFixedParams(
    this GH_Component component,
    GH_IReader reader,
    Func<GH_IReader, bool> baseRead
  )
  {
    List<IGH_Param> fixedInputs = component.Params.Input.ToList();
    List<IGH_Param> fixedOutputs = component.Params.Output.ToList();

    bool result = baseRead(reader);

    bool isDiscarded = component.Params.Input.Count == 0 && component.Params.Output.Count == 0;
    if (!isDiscarded)
    {
      return result;
    }

    RestoreSide(reader, "param_input", fixedInputs, component.Params.RegisterInputParam);
    RestoreSide(reader, "param_output", fixedOutputs, component.Params.RegisterOutputParam);
    component.Params.OnParametersChanged();
    component.Attributes?.ExpireLayout();

    return result;
  }

  /// <summary>
  /// Reads component data, providing backwards compatibility for components that have gained inputs since the data was
  /// written to the file.
  /// </summary>
  /// <remarks>
  /// Grasshopper matches "param_input" chunks to parameters by position, so an input added anywhere but the end shifts
  /// every input after it onto the wrong chunk.
  /// </remarks>
  public static bool ReadRealigningAddedInputs(
    this GH_Component component,
    GH_IReader reader,
    Func<GH_IReader, bool> baseRead
  )
  {
    if (!component.TryGetInputsToRealign(reader, out List<(int Index, IGH_Param Param)> addedInputs))
    {
      // No repair needed
      return baseRead(reader);
    }

    foreach ((int _, IGH_Param param) in addedInputs)
    {
      component.Params.UnregisterInputParameter(param, isolate: false);
    }

    bool result = baseRead(reader);

    foreach ((int index, IGH_Param param) in addedInputs)
    {
      component.Params.RegisterInputParam(param, index);
    }
    component.Params.OnParametersChanged();
    component.Attributes?.ExpireLayout();

    return result;
  }

  /// <summary>
  /// Inputs added since the file was written, when setting them aside is all that stands between the file's inputs and
  /// this component's.
  /// </summary>
  private static bool TryGetInputsToRealign(
    this GH_Component component,
    GH_IReader reader,
    out List<(int Index, IGH_Param Param)> addedInputs
  )
  {
    List<GH_IReader> archived = ArchivedParams(reader, "param_input");
    HashSet<string> archivedNames = new(archived.Select(ArchivedName));
    addedInputs = component
      .Params.Input.Select((param, index) => (Index: index, Param: param))
      .Where(entry => !archivedNames.Contains(entry.Param.Name))
      .ToList();

    bool hasArchivedInputs = archived.Count > 0;
    bool hasAddedInputs = addedInputs.Count > 0;
    bool areAddedInputsTheOnlyDifference = component.Params.Input.Count - addedInputs.Count == archived.Count;

    return hasArchivedInputs && hasAddedInputs && areAddedInputsTheOnlyDifference;
  }

  private static void RestoreSide(
    GH_IReader reader,
    string chunkName,
    List<IGH_Param> fixedParams,
    Func<IGH_Param, bool> register
  )
  {
    GH_IReader?[] matches = MatchArchivedParams(fixedParams, ArchivedParams(reader, chunkName));

    for (int i = 0; i < fixedParams.Count; i++)
    {
      register(fixedParams[i]);

      if (matches[i] is GH_IReader chunk)
      {
        ReadPreservingDefinition(fixedParams[i], chunk);
      }
    }
  }

  /// <summary>
  /// The archived parameter each of <paramref name="fixedParams"/> was saved as: matched by name where the file has
  /// that name, by position otherwise, and null where the file has nothing left to offer it.
  /// </summary>
  private static GH_IReader?[] MatchArchivedParams(List<IGH_Param> fixedParams, List<GH_IReader> archived)
  {
    GH_IReader?[] matches = new GH_IReader?[fixedParams.Count];
    HashSet<GH_IReader> matchedByName = new();

    for (int i = 0; i < fixedParams.Count; i++)
    {
      matches[i] = archived.FirstOrDefault(chunk => ArchivedName(chunk) == fixedParams[i].Name);
      if (matches[i] is GH_IReader named)
      {
        matchedByName.Add(named);
      }
    }

    for (int i = 0; i < fixedParams.Count; i++)
    {
      if (matches[i] is null && i < archived.Count && !matchedByName.Contains(archived[i]))
      {
        matches[i] = archived[i];
      }
    }

    return matches;
  }

  /// <summary>
  /// Reads the instance guid, wires, data and user settings recorded for <paramref name="param"/>, keeping the
  /// definition this component registered it with.
  /// </summary>
  private static void ReadPreservingDefinition(IGH_Param param, GH_IReader chunk)
  {
    var definition = (param.Name, param.NickName, param.Description, param.Access, param.Optional);
    try
    {
      param.Read(chunk);
    }
    catch (Exception e) when (!e.IsFatal())
    {
      // one unreadable parameter is better than a component with none
    }
    (param.Name, param.NickName, param.Description, param.Access, param.Optional) = definition;
  }

  private static List<GH_IReader> ArchivedParams(GH_IReader reader, string chunkName)
  {
    List<GH_IReader> archived = new();
    for (int i = 0; reader.FindChunk(chunkName, i) is GH_IReader chunk; i++)
    {
      archived.Add(chunk);
    }
    return archived;
  }

  private static string ArchivedName(GH_IReader chunk)
  {
    string name = string.Empty;
    chunk.TryGetString("Name", ref name);
    return name;
  }
}
