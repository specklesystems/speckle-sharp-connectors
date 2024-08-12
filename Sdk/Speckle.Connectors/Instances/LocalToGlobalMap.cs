﻿using Speckle.DoubleNumerics;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Utils.Instances;

public record LocalToGlobalMap(TraversalContext TraversalContext, Base AtomicObject, List<Matrix4x4> Matrix);