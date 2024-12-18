namespace Speckle.Converters.Common.Registration;

public interface IConverterManager<T>
{
  public string Name { get; }
  public ConverterResult<T> ResolveConverter(Type type, bool recursive = true);
}
