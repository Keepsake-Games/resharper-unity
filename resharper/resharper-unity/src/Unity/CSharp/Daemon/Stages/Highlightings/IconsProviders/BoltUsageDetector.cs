using System.Collections.Generic;
using JetBrains.Annotations;
using JetBrains.Application.Parts;
using JetBrains.Application.UI.Controls.BulbMenu.Items;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Errors;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.ContextSystem;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.PerformanceCriticalCodeAnalysis.ContextSystem;
using JetBrains.ReSharper.Plugins.Unity.Resources;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.BoltUsages;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Util;
using JetBrains.Util.Collections;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.Highlightings.IconsProviders
{
    [SolutionComponent(Instantiation.DemandAnyThreadSafe)]
    public class BoltUsageDetector : UnityDeclarationHighlightingProviderBase
    {
        protected readonly BoltUsagesElementContainer BoltUsagesElementContainer;

        public BoltUsageDetector(ISolution solution, IApplicationWideContextBoundSettingStore settingsStore,
            BoltUsagesElementContainer boltUsagesElementContainer,
                                    PerformanceCriticalContextProvider contextProvider)
            : base(solution, settingsStore, contextProvider)
        {
            BoltUsagesElementContainer = boltUsagesElementContainer;
        }

        public override bool AddDeclarationHighlighting(IDeclaration treeNode, IHighlightingConsumer consumer,
                                                        IReadOnlyCallGraphContext context)
        {
            var declaredElement = treeNode.DeclaredElement;
            var method = declaredElement as IMethod;

            // Support accessors and properties and fields and such

            return method != null && TryAddMethodHighlighting(treeNode, consumer, context, method);
        }

        private bool TryAddMethodHighlighting(IDeclaration treeNode, IHighlightingConsumer consumer, IReadOnlyCallGraphContext context,
            IMethod method)
        {
            var boltUsagesCount = BoltUsagesElementContainer.GetAssetUsagesCount(method, out var estimated);

            if (estimated || boltUsagesCount > 0)
            {
                AddBoltUsageHighlighting(treeNode, consumer, context);
                return true;
            }

            return false;
        }

        private void AddBoltUsageHighlighting([NotNull] ITreeNode treeNode,
                                                 [NotNull] IHighlightingConsumer consumer,
                                                 IReadOnlyCallGraphContext context)
        {
            AddHighlighting(consumer, treeNode as ICSharpDeclaration, Strings.BoltUsageDetector_AddBoltUsageHighlighting_Text, Strings.BoltUsageDetector_AddBoltUsageHighlighting_Tooltip, context);
        }

        protected override void AddHighlighting(IHighlightingConsumer consumer, ICSharpDeclaration element, string text,
            string tooltip, IReadOnlyCallGraphContext context)
        {
            consumer.AddImplicitConfigurableHighlighting(element);
            if (!IconProviderUtil.ShouldShowGutterMarkIcon(SettingsStore.BoundSettingsStore))
                return;

            var isIconHot = element.HasHotIcon(ContextProvider, SettingsStore.BoundSettingsStore, context);

            var highlighting = isIconHot
                ? new UnityHotGutterMarkInfo(GetActions(element), element, tooltip)
                : (IHighlighting) new UnityGutterMarkInfo(GetActions(element), element, tooltip);
            consumer.AddHighlighting(highlighting);
        }

        protected override IEnumerable<BulbMenuItem> GetActions(ICSharpDeclaration declaration)
        {
            return EnumerableCollection<BulbMenuItem>.Empty;
        }
    }
}