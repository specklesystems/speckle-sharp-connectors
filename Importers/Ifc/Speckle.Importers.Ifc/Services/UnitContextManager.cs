using Speckle.InterfaceGenerator;

namespace Speckle.Importers.Ifc.Services;

[GenerateAutoInterface]
public sealed class UnitContextManager : IUnitContextManager
{
  public string? Units { get; set; }
}
