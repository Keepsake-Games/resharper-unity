using System.Collections.Generic;
using JetBrains.Annotations;
using JetBrains.Application.PersistentMap;
using JetBrains.Serialization;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.BoltUsages
{

    [PolymorphicMarshaller]
    public class BoltUsagesDataElement : IUnityAssetDataElement
    {
        public readonly List<BoltUsageData> BoltUsages;

        [UsedImplicitly] public static UnsafeReader.ReadDelegate<object> ReadDelegate = Read;

        private static object Read(UnsafeReader reader)
        {
            var count = reader.ReadInt32();
            var methods = new List<BoltUsageData>(count);
            for (int i = 0; i < count; i++)
                methods.Add(BoltUsageData.ReadFrom(reader));
            
            var result =  new BoltUsagesDataElement(methods);
            return result;
        }

        [UsedImplicitly]
        public static UnsafeWriter.WriteDelegate<object> WriteDelegate = (w, o) => Write(w, o as BoltUsagesDataElement);

        private static void Write(UnsafeWriter writer, BoltUsagesDataElement value)
        {
            writer.Write(value.BoltUsages.Count);
            foreach (var v in value.BoltUsages)
            {
                v.WriteTo(writer);
            }
        }

        public BoltUsagesDataElement() : this(new List<BoltUsageData>())
        {
        }

        private BoltUsagesDataElement(List<BoltUsageData> boltUsageData)
        {
            BoltUsages = boltUsageData;
        }

        
        public string ContainerId => nameof(BoltUsagesElementContainer);
        public void AddData(object result)
        {
            if (result == null)
                return;

            var buildResult = (BoltUsagesBuildResult) result;
            var usages = buildResult.BoltUsageData;
            foreach (var usage in usages)
            {
                BoltUsages.Add(usage);
            }
        }
    }

}