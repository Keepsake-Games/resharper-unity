using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.References;
using JetBrains.Serialization;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.BoltUsages
{

    public class BoltUsageData
    {
        public string Name { get; }
        public LocalReference OwningScriptLocation { get; }

        public string UserGraphName { get; }
        public IReadOnlyList<BoltGraphMethodUsages> Calls { get; }

        public BoltUsageData(string name, LocalReference owningScriptLocation, string userGraphName, IEnumerable<BoltGraphMethodUsages> calls)
        {
            Name = name;
            OwningScriptLocation = owningScriptLocation;
            UserGraphName = userGraphName;
            Calls = calls.ToList();
        }
        
        public static BoltUsageData ReadFrom(UnsafeReader reader)
        {
            var name = reader.ReadString();
            var location = HierarchyReferenceUtil.ReadLocalReferenceFrom(reader);
            var userGraphName = reader.ReadString();
            var count = reader.ReadInt();
            var calls = new List<BoltGraphMethodUsages>();
            for (int i = 0; i < count; i++)
            {
                calls.Add(BoltGraphMethodUsages.ReadFrom(reader));
            }

            return new BoltUsageData(name, location, userGraphName, calls);
        }

        public void WriteTo(UnsafeWriter writer)
        {
            writer.Write(Name);
            OwningScriptLocation.WriteTo(writer);
            writer.Write(UserGraphName);
            writer.Write(Calls.Count);
            foreach (var call in Calls)
            {
                call.WriteTo(writer);
            }
        }
    }

}