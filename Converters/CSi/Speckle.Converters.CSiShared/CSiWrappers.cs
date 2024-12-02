namespace Speckle.Converters.CSiShared;

public interface ICSiWrapper
{
  string Name { get; set; }
  int ObjectType { get; }
}

/// <summary>
/// Based on GetSelected() returns of objectType and objectName, we need to create a CSiWrapper object.
/// </summary>
/// <remarks>
/// Creating a class that can be used to pass a type to the converter.
/// Since the API only provides a framework for us to query the model, we don't get instances.
/// The types are the same for both SAP 2000 and ETABS.
/// </remarks>
public abstract class CSiWrapperBase : ICSiWrapper
{
  public required string Name { get; set; }
  public abstract int ObjectType { get; }
}

public class CSiJointWrapper : CSiWrapperBase
{
  public override int ObjectType => 1;
}

public class CSiFrameWrapper : CSiWrapperBase
{
  public override int ObjectType => 2;
}

public class CSiCableWrapper : CSiWrapperBase
{
  public override int ObjectType => 3;
}

public class CSiTendonWrapper : CSiWrapperBase
{
  public override int ObjectType => 4;
}

public class CSiShellWrapper : CSiWrapperBase
{
  public override int ObjectType => 5;
}

public class CSiSolidWrapper : CSiWrapperBase
{
  public override int ObjectType => 6;
}

public class CSiLinkWrapper : CSiWrapperBase
{
  public override int ObjectType => 7;
}

/// <summary>
/// ObjectType specific wrappers created during bindings.
/// </summary>
/// <remarks>
/// Switch statements based off of the objectType int return.
/// Used in the connectors and allows converters to be resolved effectively.
/// </remarks>
public static class CSiWrapperFactory
{
  public static ICSiWrapper Create(int objectType, string name) =>
    objectType switch
    {
      1 => new CSiJointWrapper { Name = name },
      2 => new CSiFrameWrapper { Name = name },
      3 => new CSiCableWrapper { Name = name },
      4 => new CSiTendonWrapper { Name = name },
      5 => new CSiShellWrapper { Name = name },
      6 => new CSiSolidWrapper { Name = name },
      7 => new CSiLinkWrapper { Name = name },
      _ => throw new ArgumentOutOfRangeException(nameof(objectType), $"Unsupported object type: {objectType}")
    };
}
