using Speckle.Core.Models;
using Speckle.Core.Models.GraphTraversal;
using Speckle.DoubleNumerics;

namespace Speckle.Connectors.Utils.Instances;

public record LocalToGlobalMap(TraversalContext TraversalContext, Base AtomicObject, List<Matrix4x4> Matrix);
