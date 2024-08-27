namespace Speckle.Converters.Common;

public interface IConversionContext<TContext>
  where TContext : IConversionContext<TContext>
{
  public TContext Duplicate();
}
