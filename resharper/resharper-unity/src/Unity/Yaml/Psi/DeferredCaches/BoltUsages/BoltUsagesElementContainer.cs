using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.Collections;
using JetBrains.Diagnostics;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Psi.Resolve;
using JetBrains.ReSharper.Plugins.Unity.UnityEditorIntegration;
using JetBrains.ReSharper.Plugins.Unity.UnityEditorIntegration.Api;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Caches;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.Elements;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.References;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.Utils;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Resolve;
using JetBrains.ReSharper.Plugins.Yaml.Psi;
using JetBrains.ReSharper.Plugins.Yaml.Psi.Tree;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.ExtensionsAPI.Resolve;
using JetBrains.ReSharper.Psi.ExtensionsAPI.Resolve.Filters;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Psi.Resolve;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Util;
using JetBrains.Util;
using JetBrains.Util.Collections;
using JetBrains.Util.dataStructures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.BoltUsages
{
    [SolutionComponent(Instantiation.DemandAnyThreadSafe)]
    public class BoltUsagesElementContainer : IUnityAssetDataElementContainer, IScriptUsagesElementContainer
    {
        private const string BoltInvokeMemberName = "Bolt.InvokeMember";

        private readonly ISolution mySolution;
        private readonly IShellLocks myShellLocks;
        private readonly AssetDocumentHierarchyElementContainer myAssetDocumentHierarchyElementContainer;
        private readonly ILogger myLogger;
        
        public BoltUsagesElementContainer(ISolution solution, IShellLocks shellLocks,
            AssetDocumentHierarchyElementContainer elementContainer, ILogger logger)
        {
            mySolution = solution;
            myShellLocks = shellLocks;
            myAssetDocumentHierarchyElementContainer = elementContainer;
            myLogger = logger;
        }

        private static readonly StringSearcher ourFlowMacroGuidSearcher = new("a040fb66244a7f54289914d98ea4ef7d", false);
        private static readonly StringSearcher ourInvokeMemberSearcher = new(BoltInvokeMemberName, false);

        private readonly OneToSetMap<IPsiSourceFile, BoltUsageData> myPsiSourceFileToUsageData = new();
        private readonly OneToCompactCountingSet<string, IPsiSourceFile> myMethodNameToFilesWithUsages = new();

        private readonly OneToCompactCountingSet<string, IPsiSourceFile> myMethodNameToFilesWithPossibleUsages = new();
        private readonly OneToCompactCountingSet<string, BoltGraphMethodUsages> myLocalMethodUsages = new();

        // for count check & find usages
        private readonly CountingSet<(string methodName, string userGraphName)> myBoltUsageCount = new();
        
        public string Id => nameof(BoltUsagesElementContainer);
        public int Order => 0;
        public IUnityAssetDataElement CreateDataElement(IPsiSourceFile sourceFile)
        {
            return new BoltUsagesDataElement();
        }

        public bool IsApplicable(IPsiSourceFile currentAssetSourceFile)
        {
            return true; // TEMP always applicable when lending UnityEvent logic below
            //return currentAssetSourceFile.IsAsset();
        }

        public object Build(IPsiSourceFile currentAssetSourceFile, AssetDocument assetDocument)
        {
            var buffer = assetDocument.Buffer;

            myLogger.Trace($"Build, running in {currentAssetSourceFile} for \n{assetDocument.Buffer.GetText()}");

            if (ourFlowMacroGuidSearcher.Find(buffer) < 0)
            {
                myLogger.Trace($"Build, no match for FlowMacro GUID in {currentAssetSourceFile.Name}");
                return new BoltUsagesBuildResult(new LocalList<BoltUsageData>());
            }

            if (ourInvokeMemberSearcher.Find(buffer) < 0)
            {
                myLogger.Trace($"Build, early out since no {BoltInvokeMemberName} in {currentAssetSourceFile.Name}");
                return new BoltUsagesBuildResult(new LocalList<BoltUsageData>());
            }

            var anchorRaw = AssetUtils.GetAnchorFromBuffer(assetDocument.Buffer);
            if (!anchorRaw.HasValue)
            {
                myLogger.Trace($"Build, anchor had no value in {currentAssetSourceFile.Name}");
                return new BoltUsagesBuildResult(new LocalList<BoltUsageData>());
            }

            var anchor = anchorRaw.Value;

            var properties = assetDocument.Document.GetUnityObjectProperties();
            var entries = properties?.Entries;
            if (entries == null)
            {
                myLogger.Trace($"Build, no UnityObject properties for asset document {assetDocument.Document}");
                return new BoltUsagesBuildResult(new LocalList<BoltUsageData>());
            }
            
            myLogger.Trace($"Build, found {entries.Value.Count} UnityObject properties for asset document {assetDocument.Document}");

            /*var scriptReference = properties.GetMapEntryValue<INode>(UnityYamlConstants.ScriptProperty)
                .ToHierarchyReference(currentAssetSourceFile) as ExternalReference?;
            if (scriptReference == null)
            {
                myLogger.Trace($"Build, no {UnityYamlConstants.ScriptProperty} amongst object properties in asset document {assetDocument.Document}");
                return new BoltUsagesBuildResult(new LocalList<BoltUsageData>());
            }*/

            var location = new LocalReference(currentAssetSourceFile.PsiStorage.PersistentIndex.NotNull("owningPsiPersistentIndex != null"), anchor);

            var result = new LocalList<BoltUsageData>();
            foreach (var entry in entries)
            {
                myLogger.Trace($"Checking if property entry is serialized Bolt data: {entry.Content.GetTextAsBuffer().GetText()}");
                if (entry.Key.GetScalarText()?.Equals("_data", StringComparison.Ordinal) ?? false)
                {
                    /*var name = entry.Key.GetScalarText();
                    myLogger.Trace($"  method name: {name}");
                    if (name == null)
                        continue;*/
                    
                    BuildRootMappingNode(currentAssetSourceFile, assetDocument, entry.Content.Value, currentAssetSourceFile.DisplayName, ref result, location);
                }
            }
            
            myLogger.Trace($"Build complete for {currentAssetSourceFile}, {result.Count} results");
            return new BoltUsagesBuildResult(result);
        }

        public void Drop(IPsiSourceFile currentAssetSourceFile, AssetDocumentHierarchyElement assetDocumentHierarchyElement, IUnityAssetDataElement unityAssetDataElement)
        {
            var element = unityAssetDataElement as BoltUsagesDataElement;
            foreach (var boltUsageData in element.BoltUsages)
            {
                var usageName = boltUsageData.Name;
                var userGraphName = boltUsageData.UserGraphName;
                myBoltUsageCount.Add((usageName, userGraphName), -boltUsageData.Calls.Count);

                foreach (var call in boltUsageData.Calls)
                {
                    myLocalMethodUsages.Remove(call.MethodName, new BoltGraphMethodUsages(boltUsageData.Name, call.MethodName, TextRange.InvalidRange, 0, call.ParameterTypes, call
                        .PsiModuleName, call.ArgumentTypeNameRange, call.TargetType));
                }
            }

            myPsiSourceFileToUsageData.RemoveKey(currentAssetSourceFile);
        }

        public void Merge(IPsiSourceFile currentAssetSourceFile, AssetDocumentHierarchyElement assetDocumentHierarchyElement, IUnityAssetDataElementPointer unityAssetsCache,
            IUnityAssetDataElement unityAssetDataElement)
        {
            var element = (unityAssetDataElement as BoltUsagesDataElement).NotNull("element != null");

            foreach (var boltUsageData in element.BoltUsages)
            {
                var usageName = boltUsageData.Name;
                var userGraphName = boltUsageData.UserGraphName;
                myBoltUsageCount.Add((usageName, userGraphName), boltUsageData.Calls.Count);
                myPsiSourceFileToUsageData.Add(currentAssetSourceFile, boltUsageData);

                foreach (var call in boltUsageData.Calls)
                {
                    myMethodNameToFilesWithUsages.Add(call.MethodName, currentAssetSourceFile);

                    myLocalMethodUsages.Add(call.MethodName, new BoltGraphMethodUsages(boltUsageData.Name, call.MethodName, TextRange.InvalidRange,
                        0, call.ParameterTypes, call.PsiModuleName, call.ArgumentTypeNameRange, call.TargetType));
                }
            }
        }

        public void Invalidate()
        {
            myBoltUsageCount.Clear();
            myMethodNameToFilesWithUsages.Clear();
            myMethodNameToFilesWithPossibleUsages.Clear();
            myPsiSourceFileToUsageData.Clear();
        }

        private void BuildRootMappingNode(IPsiSourceFile currentAssetSourceFile, AssetDocument assetDocument,
            INode node, string userGraphName, ref LocalList<BoltUsageData> result, LocalReference location)
        {
            if (node is not IBlockMappingNode rootMap)
            {
                myLogger.Trace($"BuildRootMappingNode: node is not IBlockMappingNode ({node.NodeType})");
                return;
            }

            var jsonNode = rootMap.GetMapEntryValue<INode>("_json");
            var json = jsonNode?.GetScalarText();
            if (json == null)
            {
                myLogger.Trace($"BuildRootMappingNode: no _json scalar node in root");
                return;
            }

            myLogger.Trace($"BuildRootMappingNode: json = {json}");

            var calls = GetCalls(currentAssetSourceFile, assetDocument, jsonNode.GetTreeStartOffset(), json, userGraphName);
            result.Add(new BoltUsageData(userGraphName, location, currentAssetSourceFile.DisplayName, calls.ToArray()));
        }
        
        private LocalList<BoltGraphMethodUsages> GetCalls(IPsiSourceFile currentAssetSourceFile,
                                                      AssetDocument assetDocument, TreeOffset jsonStartOffset, string json, string userGraphName)
        {
            var result = new LocalList<BoltGraphMethodUsages>();

            myLogger.Trace($"GetCalls: looking for call in graph in {userGraphName}");


            var jsonLines = json.SplitByNewLine();
            var reader = new JsonTextReader(new StringReader(json));
            TextRange? currentRange = null;
            var currentObjectDepth = 0;
            var isReadingMemberObject = false;
            string currentElementType = null;
            string currentMemberName = null;
            string currentMemberTargetType = null;
            var currentParameterTypes = new LocalList<string>();
            TextRange? currentParameterTypesRange = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.StartObject)
                {
                    currentObjectDepth++;
                }
                else if (reader.TokenType == JsonToken.EndObject)
                {
                    if (isReadingMemberObject)
                    {
                        isReadingMemberObject = false;
                    }
                    else if (currentObjectDepth == 3) // 1: graph, 2: elements (and siblings..), 3: object in elements
                    {
                        if (string.Equals(currentElementType, BoltInvokeMemberName, StringComparison.Ordinal)
                            && !string.IsNullOrEmpty(currentMemberName) && !string.IsNullOrEmpty(currentMemberTargetType))
                        {
                            myLogger.Trace($"GetCalls: Resolving {currentMemberTargetType} using Psi symbol cache..", currentMemberName, currentMemberTargetType);
                            var cache = mySolution.GetPsiServices().Symbols.GetSymbolScope(LibrarySymbolScope.FULL, true);
                            var typeElement = cache.GetTypeElementByCLRName(currentMemberTargetType);
                            myLogger.Trace($"GetCalls: .. typeElement is {typeElement?.ToString() ?? "null"}", currentMemberName, currentMemberTargetType);
                            var psiModule = default(IPsiModule);
                            if (typeElement != null)
                            {
                                myLogger.Trace($"GetCalls: .. module is {typeElement.Module}", currentMemberName, currentMemberTargetType);
                                psiModule = typeElement.Module;
                            }
                
                            var typeRange = TextRange.InvalidRange;
                            if (currentParameterTypes.Count > 0)
                            {
                                typeRange = currentParameterTypesRange.Value;
                            }

                            myLogger.Trace($"GetCalls: saving call to method {currentMemberName} (on type {currentMemberTargetType}, module {psiModule}) from graph {userGraphName} ({currentRange.Value} in {currentAssetSourceFile})");
                            result.Add(new BoltGraphMethodUsages(userGraphName, currentMemberName, currentRange.Value,
                                currentAssetSourceFile.PsiStorage.PersistentIndex.NotNull("owningPsiPersistentIndex != null"),
                                currentParameterTypes.ToArray(), psiModule?.Name, typeRange, currentMemberTargetType));
                        }

                        currentRange = null;
                        currentElementType = null;
                        currentMemberName = null;
                        currentMemberTargetType = null;
                        currentParameterTypes = new LocalList<string>();
                        currentParameterTypesRange = null;
                    }

                    currentObjectDepth--;
                }
                else if (reader.TokenType == JsonToken.PropertyName)
                {
                    if (isReadingMemberObject)
                    {
                        if (string.Equals((string)reader.Value, "name", StringComparison.Ordinal))
                        {
                            var offsetStart = CurrentBufferPosition(assetDocument, jsonStartOffset, reader, jsonLines) + 2;
                            currentMemberName = reader.ReadAsString();
                            var offsetEnd = CurrentBufferPosition(assetDocument, jsonStartOffset, reader, jsonLines);
                            currentRange = new TextRange(offsetStart, offsetEnd);
                        }
                        else if (string.Equals((string)reader.Value, "targetType", StringComparison.Ordinal))
                        {
                            currentMemberTargetType = reader.ReadAsString();
                        }
                        else if (string.Equals((string)reader.Value, "parameterTypes", StringComparison.Ordinal))
                        {
                            var offsetStart = CurrentBufferPosition(assetDocument, jsonStartOffset, reader, jsonLines);
                            while (reader.TokenType != JsonToken.EndArray)
                            {
                                if (reader.TokenType == JsonToken.String)
                                {
                                    currentParameterTypes.Add((string)reader.Value);
                                }

                                reader.Read();
                            }

                            var offsetEnd = CurrentBufferPosition(assetDocument, jsonStartOffset, reader, jsonLines);
                            currentParameterTypesRange = new TextRange(offsetStart, offsetEnd);
                        }
                    }
                    else
                    {
                        if (string.Equals((string)reader.Value, "$type", StringComparison.Ordinal))
                        {
                            currentElementType = reader.ReadAsString();
                        }
                        else if (string.Equals((string)reader.Value, "member", StringComparison.Ordinal))
                        {
                            isReadingMemberObject = true;
                            continue;
                        }
                    }
                }
            }

            /*var graph = (JObject)JObject.Parse(json)["graph"];
            if (graph == default)
            {
                myLogger.Trace($"BuildRootMappingNode: {result.Count} results, no graph");
                return result;
            }

            var elementsToken = graph.GetValueSafe("elements");
            if (elementsToken == default)
            {
                myLogger.Trace($"BuildRootMappingNode: {result.Count} results, no elements");
                return result;
            }

            var elements = (JArray)elementsToken;
            myLogger.Trace($"GetCalls: elements = {elements.Count} results");
            foreach (var elToken in elements)
            {
                var el = (JObject)elToken;

                var elementTypeToken = el.GetValueSafe("$type");
                if (elementTypeToken == default || !string.Equals((string)elementTypeToken, BoltInvokeMemberName, StringComparison.Ordinal))
                {
                    myLogger.Trace($"GetCalls: .. no $type or not {BoltInvokeMemberName}");
                    continue;
                }
                
                var memberToken = el.GetValueSafe("member");
                if (memberToken == default)
                {
                    myLogger.Trace($"GetCalls: {result.Count} results, no member object in {el}");
                    continue;
                }
                
                var member = (JObject)memberToken;
                
                var methodName = (string)member["name"];
                if (methodName == null)
                    continue;
                
                var targetType = (string)member["targetType"];
                if (targetType == null)
                    continue;

                myLogger.Trace($"GetCalls: Resolving {targetType} using Psi symbol cache..", methodName, targetType);
                var cache = mySolution.GetPsiServices().Symbols.GetSymbolScope(LibrarySymbolScope.FULL, true);
                var typeElement = cache.GetTypeElementByCLRName(targetType);
                myLogger.Trace($"GetCalls: .. typeElement is {typeElement?.ToString() ?? "null"}", methodName, targetType);
                var psiModule = default(IPsiModule);
                if (typeElement != null)
                {
                    myLogger.Trace($"GetCalls: .. module is {typeElement.Module}", methodName, targetType);
                    psiModule = typeElement.Module;
                }
                
                var parameterTypes = (JArray)member["parameterTypes"];
                var types = parameterTypes?.Select(p => (string)p).ToArray() ?? [];
                var typeRange = TextRange.InvalidRange;
                if (types.Length > 0)
                {
                    typeRange = new TextRange(assetDocument.StartOffset);
                }
                
                var range = new TextRange(assetDocument.StartOffset);

                myLogger.Trace($"GetCalls: saving call to method {methodName} (on type {targetType}, module {psiModule}) from graph {userGraphName} ({range} in {currentAssetSourceFile})");
                result.Add(new BoltGraphMethodUsages(userGraphName, methodName, range,
                    currentAssetSourceFile.PsiStorage.PersistentIndex.NotNull("owningPsiPersistentIndex != null"),
                    types, psiModule?.Name, typeRange, targetType));
            }*/

            return result;
        }

        private static int CurrentBufferPosition(AssetDocument assetDocument, TreeOffset jsonStartOffset, JsonTextReader reader, string[] jsonLines)
        {
            var pos = assetDocument.StartOffset + jsonStartOffset.Offset;
            for (var i = 1; i < reader.LineNumber; ++i)
            {
                // seems to drift 1 per line, I'm guessing its the newline character or so, so compensate here
                pos += jsonLines[i - 1].Length + 1;
            }

            pos += reader.LinePosition;

            return pos;
        }

        public int GetAssetUsagesCount(IDeclaredElement declaredElement, out bool estimatedResult)
        {
            if (declaredElement is IProperty property)
            {
                var getter = property.Getter;
                var setter = property.Setter;

                var count = 0;
                estimatedResult = false;
                if (getter != null)
                {
                    count += GetAssetUsagesCountInner(getter, out var getterEstimated);
                    estimatedResult |= getterEstimated;
                }

                if (setter != null)
                {
                    count += GetAssetUsagesCountInner(setter, out var setterEstimated);
                    estimatedResult |= setterEstimated;
                }

                return count;
            }

            return GetAssetUsagesCountInner(declaredElement, out estimatedResult);
        }
        
        /*public int GetUsageCountForMethod(ITypeOwner typeOwner, out bool isEstimated)
        {
            myShellLocks.AssertReadAccessAllowed();

            isEstimated = false;
            var containingType = typeOwner?.GetContainingType();
            if (containingType == null)
                return 0;

            var guid = AssetUtils.GetGuidFor(myGuidCache, containingType);
            if (guid == null)
                return 0;

            var result = 0;
            foreach (var name in AssetUtils.GetAllNamesFor(typeOwner))
            {
                result += myBoltUsageCount.GetCount((name, guid.Value));
            }

            return result;
        }*/

        private int GetAssetUsagesCountInner(IDeclaredElement declaredElement, out bool estimatedResult)
        {
            myShellLocks.AssertReadAccessAllowed();
            estimatedResult = false;
            if (!(declaredElement is IClrDeclaredElement clrDeclaredElement))
                return 0;

            if (myMethodNameToFilesWithPossibleUsages.GetOrEmpty(declaredElement.ShortName).Count > 0)
                estimatedResult = true;

            const int maxProcessCount = 5;
            if (myLocalMethodUsages.GetOrEmpty(declaredElement.ShortName).Count > maxProcessCount)
                estimatedResult = true;

            var usageCount = 0;
            foreach (var (boltUsage, c) in myLocalMethodUsages.GetOrEmpty(declaredElement.ShortName).Take(maxProcessCount))
            {
                var solution = declaredElement.GetSolution();
                var module = clrDeclaredElement.Module;

                var symbolTable = GetReferenceSymbolTable(solution, module, boltUsage);
                var resolveResult = symbolTable.GetResolveResult(boltUsage.MethodName);
                if (resolveResult.ResolveErrorType == ResolveErrorType.OK && Equals(resolveResult.DeclaredElement, declaredElement))
                {
                    usageCount += c;
                }
            }

            return usageCount;
        }

        public IEnumerable<BoltUsageFindResult> GetAssetUsagesFor(IPsiSourceFile psiSourceFile, IDeclaredElement declaredElement)
        {
            myShellLocks.AssertReadAccessAllowed();
            var result = new List<BoltUsageFindResult>();

            myLogger.Trace($"Getting asset method data for {psiSourceFile}...");
            foreach (var (owningScriptLocation, methodData) in GetAssetMethodDataFor(psiSourceFile))
            {
                myLogger.Trace($"AssetMethodData for {psiSourceFile}: {methodData.MethodName}");
                var symbolTable = GetReferenceSymbolTable(psiSourceFile.GetSolution(), psiSourceFile.GetPsiModule(), methodData);
                myLogger.Trace($"  symbol table: {string.Join(", ", symbolTable.Names())}");
                var resolveResult = symbolTable.GetResolveResult(methodData.MethodName);
                myLogger.Trace($"  resolve result error type: {resolveResult}");
                if (resolveResult.ResolveErrorType == ResolveErrorType.OK)
                {
                    myLogger.Trace($"  resolve result declared element: {resolveResult.DeclaredElement}");
                }
                if (resolveResult.ResolveErrorType == ResolveErrorType.OK && Equals(resolveResult.DeclaredElement, declaredElement))
                {
                    result.Add(new BoltUsageFindResult(psiSourceFile, declaredElement, methodData, owningScriptLocation));
                }
            }

            myLogger.Trace($"{psiSourceFile.Name} --> {result.Count} usage(s)");

            return result;
        }
        
        private IEnumerable<(LocalReference owningScriptLocation, BoltGraphMethodUsages method)> GetAssetMethodDataFor(IPsiSourceFile psiSourceFile)
        {
            foreach (var data in myPsiSourceFileToUsageData.GetValuesSafe(psiSourceFile))
            foreach (var call in data.Calls)
                yield return (data.OwningScriptLocation, call);
        }

        private ISymbolTable GetReferenceSymbolTable(ISolution solution, IPsiModule psiModule, BoltGraphMethodUsages boltGraphMethodUsages)
        {
            var cache = mySolution.GetPsiServices().Symbols.GetSymbolScope(LibrarySymbolScope.FULL, true);
            var typeElement = cache.GetTypeElementByCLRName(boltGraphMethodUsages.TargetType);
            if (typeElement == null)
                return EmptySymbolTable.INSTANCE;

            var symbolTable = ResolveUtil.GetSymbolTableByTypeElement(typeElement, SymbolTableMode.FULL, psiModule);
            // TODO match on param types too
            var method = typeElement.Methods.FirstOrDefault(m => m.ShortName.Equals(boltGraphMethodUsages.MethodName) && ArrayUtil.StructuralEquals(m.Parameters.Select(p => p.Type.GetTypeElement()?.GetClrName().FullName).ToArray(), boltGraphMethodUsages.ParameterTypes));
            if (method == null)
                return EmptySymbolTable.INSTANCE;

            var parameterTypes = new FrugalLocalList<IType>();
            var parameterNames = new FrugalLocalList<string>();
            foreach (var parameter in method.Parameters)
            {
                parameterTypes.Add(parameter.Type);
                parameterNames.Add(parameter.ShortName);
            }

            var methodSignature = new MethodSignature(method.ReturnType, method.IsStatic,
                parameterTypes.AsIReadOnlyList(), parameterNames.AsIReadOnlyList());

            return symbolTable.Filter(boltGraphMethodUsages.MethodName, IsMethodFilter.INSTANCE, OverriddenFilter.INSTANCE, new ExactNameFilter(boltGraphMethodUsages.MethodName),
                new MethodSignatureFilter(UnityResolveErrorType.UNITY_STRING_LITERAL_REFERENCE_INCORRECT_SIGNATURE_WARNING, methodSignature));
        }

        public LocalList<IPsiSourceFile> GetPossibleFilesWithUsage(IDeclaredElement element)
        {
            if (element == null)
                return new LocalList<IPsiSourceFile>();

            var result = new LocalList<IPsiSourceFile>();
            var shortName = element.ShortName;

            foreach (var sourceFile in myMethodNameToFilesWithUsages.GetValues(shortName))
                result.Add(sourceFile);

            foreach (var sourceFile in myMethodNameToFilesWithPossibleUsages.GetValues(shortName))
                result.Add(sourceFile);

            return result;
        }

        public IEnumerable<IScriptUsage> GetScriptUsagesFor(IPsiSourceFile sourceFile, ITypeElement typeElement)
        {
            var result = new List<IScriptUsage>();
            return result;
        }

        public LocalList<IPsiSourceFile> GetPossibleFilesWithScriptUsages(ITypeElement typeElement)
        {
            var files = new LocalList<IPsiSourceFile>();
            return files;
        }

        public int GetScriptUsagesCount(IClassLikeDeclaration classLikeDeclaration, out bool estimatedResult)
        {
            estimatedResult = false;
            if (classLikeDeclaration.DeclaredElement is not IClass element)
                return 0;

            return GetScriptUsagesCount(element, out estimatedResult);
        }
        
        public int GetScriptUsagesCount(IClass element, out bool estimatedResult)
        {
            estimatedResult = false;

            return 0;
        }
    }

    /*internal class PositionTrackingStringReader : StringReader
    {
        public PositionTrackingStringReader(string s) : base(s)
        {
            Position = 0;
        }

        public int Position { get; private set; }

        public override int Read()
        {
            var result = base.Read();
            if (result != -1) Position++;
            return result;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            var result = base.Read(buffer, index, count);
            if (result > 0) Position += result;
            return result;
        }

        public override string ReadLine()
        {
            var result = base.ReadLine();
            if (result != null) Position += result.Length + Environment.NewLine.Length; // Include newline characters
            return result;
        }

        public override string ReadToEnd()
        {
            var result = base.ReadToEnd();
            Position += result!.Length;
            return result;
        }
    }*/
}