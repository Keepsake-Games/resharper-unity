using JetBrains.ReSharper.Plugins.Unity.Resources.Icons;
using JetBrains.ReSharper.Plugins.Unity.Utils;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.References;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.BoltUsages;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Pointers;
using JetBrains.UI.Icons;
using JetBrains.UI.RichText;
using JetBrains.Util;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Feature.Services.Navigation
{
    public class BoltUsageOccurrence : UnityAssetOccurrence, IAssetOccurrenceWithTextOccurrence
    {
        public readonly BoltGraphMethodUsages MethodUsages;

        public BoltUsageOccurrence(IPsiSourceFile sourceFile, IDeclaredElementPointer<IDeclaredElement> declaredElement,
            LocalReference owningElementLocation, BoltGraphMethodUsages methodUsages)
            : base(sourceFile, declaredElement, owningElementLocation, false)
        {
            MethodUsages = methodUsages;
        }


        public override string ToString()
        {
            return $"m_MethodName: {MethodUsages.MethodName}";
        }

        public override IconId GetIcon()
        {
            return InsightUnityIcons.InsightBolt.Id;
        }

        public override RichText GetDisplayText()
        {
            return SourceFile.GetLocation().NameWithoutExtension;
        }

        public IPsiSourceFile GetSourceFile()
        {
            return SourceFile;
        }

        public TextRange RenameTextRange => MethodUsages.TextRangeOwnerPsiPersistentIndex;
    }
}