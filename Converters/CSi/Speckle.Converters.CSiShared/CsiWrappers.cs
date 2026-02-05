using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared;

public interface ICsiWrapper
{
  string Name { get; set; }
  ModelObjectType ObjectType { get; }
  string ObjectName { get; }
}

/// <summary>
/// Based on the GetSelected() returns of objectType and objectName, we need to create a CSiWrapper object.
/// </summary>
/// <remarks>
/// Creating a class that can be used to pass a type to the converter.
/// Since the API only provides a framework for us to query the model, we don't get instances.
/// The types are the same for both SAP 2000 and ETABS.
/// </remarks>
public abstract class CsiWrapperBase : ICsiWrapper
{
  public required string Name { get; set; }
  public abstract ModelObjectType ObjectType { get; }
  public abstract string ObjectName { get; }
}

public class CsiJointWrapper : CsiWrapperBase
{
  public override ModelObjectType ObjectType => ModelObjectType.JOINT;
  public override string ObjectName => ModelObjectType.JOINT.ToString();
}

public class CsiFrameWrapper : CsiWrapperBase
{
  public override ModelObjectType ObjectType => ModelObjectType.FRAME;
  public override string ObjectName => ModelObjectType.FRAME.ToString();
}

public class CsiCableWrapper : CsiWrapperBase
{
  public override ModelObjectType ObjectType => ModelObjectType.CABLE;
  public override string ObjectName => ModelObjectType.CABLE.ToString();
}

public class CsiTendonWrapper : CsiWrapperBase
{
  public override ModelObjectType ObjectType => ModelObjectType.TENDON;
  public override string ObjectName => ModelObjectType.TENDON.ToString();
}

public class CsiShellWrapper : CsiWrapperBase
{
  public override ModelObjectType ObjectType => ModelObjectType.SHELL;
  public override string ObjectName => ModelObjectType.SHELL.ToString();
}

public class CsiSolidWrapper : CsiWrapperBase
{
  public override ModelObjectType ObjectType => ModelObjectType.SOLID;
  public override string ObjectName => ModelObjectType.SOLID.ToString();
}

public class CsiLinkWrapper : CsiWrapperBase
{
  public override ModelObjectType ObjectType => ModelObjectType.LINK;
  public override string ObjectName => ModelObjectType.LINK.ToString();
}

/// <summary>
/// ObjectType specific wrappers created during bindings.
/// </summary>
/// <remarks>
/// Switch statements based off of the objectType int return.
/// Used in the connectors and allows converters to be resolved effectively.
/// </remarks>
public static class CsiWrapperFactory
{
  public static ICsiWrapper Create(int objectType, string name) =>
    objectType switch
    {
      1 => new CsiJointWrapper { Name = name },
      2 => new CsiFrameWrapper { Name = name },
      3 => new CsiCableWrapper { Name = name }, // TODO: CsiCableWrapper
      4 => new CsiTendonWrapper { Name = name }, // TODO: CsiTendonWrapper
      5 => new CsiShellWrapper { Name = name },
      6 => new CsiSolidWrapper { Name = name }, // TODO: CsiSolidWrapper
      7 => new CsiLinkWrapper { Name = name }, // TODO: CsiLinkWrapper
      _ => throw new ArgumentOutOfRangeException(nameof(objectType), $"Unsupported object type: {objectType}"),
    };
}
