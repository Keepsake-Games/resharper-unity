using JetBrains.Util;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.BoltUsages
{

    public class BoltUsagesBuildResult
    {
        public BoltUsagesBuildResult(LocalList<BoltUsageData> boltUsageData)
        {
            BoltUsageData = boltUsageData;
        }

        public LocalList<BoltUsageData> BoltUsageData { get; }
    }

}