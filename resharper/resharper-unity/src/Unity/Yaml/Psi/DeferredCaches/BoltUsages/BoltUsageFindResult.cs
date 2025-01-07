using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.References;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Search;
using JetBrains.ReSharper.Psi;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.BoltUsages
{

    public class BoltUsageFindResult : UnityAssetFindResult
    {
        public BoltGraphMethodUsages BoltGraphMethodUsages { get; }

        public BoltUsageFindResult(IPsiSourceFile sourceFile, IDeclaredElement declaredElement,  BoltGraphMethodUsages boltGraphMethodUsages, LocalReference owningElementLocation)
            : base(sourceFile, declaredElement, owningElementLocation)
        {
            BoltGraphMethodUsages = boltGraphMethodUsages;
        }

        protected bool Equals(BoltUsageFindResult other)
        {
            return base.Equals(other) && BoltGraphMethodUsages.Equals(other.BoltGraphMethodUsages);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((BoltUsageFindResult) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (base.GetHashCode() * 397) ^ BoltGraphMethodUsages.GetHashCode();
            }
        }
    }

}