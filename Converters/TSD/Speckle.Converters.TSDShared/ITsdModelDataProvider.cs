using TSD.API.Remoting.Structure;
using TSD.API.Remoting.Units;

namespace Speckle.Converters.TSDShared;

public interface ITsdModelDataProvider
{
  Task<IModel?> GetModelAsync();

  Task<IReadOnlyList<double>> ConvertFromBaseAsync(IReadOnlyList<double> values, IUnitBase unit);
}
