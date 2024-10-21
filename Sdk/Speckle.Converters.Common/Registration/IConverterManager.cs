namespace Speckle.Converters.Common.Registration;

public interface IConverterManager<T>
{
  public string Name { get; }
  public T ResolveConverter(Type type, bool recursive = false);
}
