using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Converters.Revit2023.ToSpeckle.Parameters;

public class ParameterDefinitionHandler
{
  private readonly IRevitConversionContextStack _contextStack;
  private readonly Dictionary<string, object> _definitions = new();

  public ParameterDefinitionHandler(IRevitConversionContextStack contextStack)
  {
    _contextStack = contextStack;
  }

  public string HandleDefinition(DB.Parameter parameter)
  {
    var definition = parameter.Definition;
    var name = definition.Name;

    if (definition is DB.InternalDefinition internalDefinition)
    {
      name = internalDefinition.BuiltInParameter.ToString();
    }

    if (_definitions.ContainsKey(name))
    {
      return name;
    }

    _definitions[name] = new Dictionary<string, object>()
    {
      ["definitionName"] = name,
      ["units"] = definition.GetDataType().TypeId,
      ["isShared"] = parameter.IsShared,
      ["isReadOnly"] = parameter.IsReadOnly,
      ["typeId"] = definition.GetGroupTypeId().TypeId
    };
    return name;
  }
}

public class ParameterExtractor
{
  private readonly ParameterDefinitionHandler _parameterDefinitionHandler;

  public ParameterExtractor(ParameterDefinitionHandler parameterDefinitionHandler)
  {
    _parameterDefinitionHandler = parameterDefinitionHandler;
  }

  public List<object> GetParameters(DB.Element element)
  {
    var paramsList = new List<object>();
    var parameters = element.Parameters;
    foreach (DB.Parameter parameter in parameters)
    {
      var name = _parameterDefinitionHandler.HandleDefinition(parameter);
      paramsList.Add(name);
    }

    return paramsList;
  }

  private object GetValue(DB.Parameter parameter)
  {
    switch (parameter.StorageType)
    {
      case DB.StorageType.Double:
        return parameter.AsDouble(); // TODO: scaling
      case DB.StorageType.Integer:
        return parameter.AsInteger();
      case DB.StorageType.String:
        return parameter.AsString();
      case DB.StorageType.ElementId:
        // TODO: return friendly name, not element id
        break;
    }
  }
}

// #pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
// #pragma warning disable IDE1006
// public class SimpleParam : Base
// {
//   public string name { get; set; }
//   public string definitionId { get; set; }
//   public object value { get; set; }
// }
// #pragma warning restore IDE1006
// #pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
