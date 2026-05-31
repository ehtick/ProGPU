using System.Collections.Generic;

namespace ProGPU.Scene
{
    public class StaticCompilationContext
    {
        private readonly Dictionary<int, object> _builders = new();
        private readonly object _lock = new();

        public void SetBuilder(int extensionId, object builder)
        {
            lock (_lock)
            {
                _builders[extensionId] = builder;
            }
        }

        public object? GetBuilder(int extensionId)
        {
            lock (_lock)
            {
                return _builders.TryGetValue(extensionId, out var builder) ? builder : null;
            }
        }
    }
}
