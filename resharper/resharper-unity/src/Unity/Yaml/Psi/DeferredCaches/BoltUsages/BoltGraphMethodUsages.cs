using System.Collections.Generic;
using JetBrains.Diagnostics;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.References;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetInspectorValues.Values;
using JetBrains.Serialization;
using JetBrains.Util;
using JetBrains.Util.Maths;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.BoltUsages
{
    public class BoltGraphMethodUsages
    {
        public string OwnerName { get; }
        public string MethodName { get; }
        public string[] ParameterTypes { get; }
        public string PsiModuleName { get; }
        public TextRange ArgumentTypeNameRange { get; }
        public string TargetType { get; }
        public TextRange TextRangeOwnerPsiPersistentIndex { get; }
        
        public OWORD TextRangeOwner { get; }
        
        public BoltGraphMethodUsages(string ownerName, string methodName,
            TextRange textRangeOwnerPsiPersistentIndex, OWORD textRangeOwner,
            string[] parameterTypes, string psiModuleName, TextRange argumentTypeNameRange,
            string targetType)
        {
            Assertion.Assert(targetType != null, "targetReference != null");
            Assertion.Assert(methodName != null, "methodName != null");
            OwnerName = ownerName;
            MethodName = methodName;
            TextRangeOwnerPsiPersistentIndex = textRangeOwnerPsiPersistentIndex;
            TextRangeOwner = textRangeOwner;
            ParameterTypes = parameterTypes;
            PsiModuleName = psiModuleName;
            ArgumentTypeNameRange = argumentTypeNameRange;
            TargetType = targetType;
        }
        
        public void WriteTo(UnsafeWriter writer)
        {
            writer.Write(OwnerName);
            writer.Write(MethodName);
            writer.Write(TextRangeOwnerPsiPersistentIndex.StartOffset);
            writer.Write(TextRangeOwnerPsiPersistentIndex.EndOffset);
            AssetUtils.WriteOWORD(TextRangeOwner, writer);
            writer.Write(ParameterTypes.Length);
            foreach (var parameterType in ParameterTypes)
            {
                writer.Write(parameterType);
            }
            writer.Write(PsiModuleName);
            writer.Write(ArgumentTypeNameRange.StartOffset);
            writer.Write(ArgumentTypeNameRange.EndOffset);
            writer.Write(TargetType);
        }
        
        public static BoltGraphMethodUsages ReadFrom(UnsafeReader reader)
        {
            var ownerName = reader.ReadString();
            var methodName = reader.ReadString();
            var textRangeStart = reader.ReadInt32();
            var textRangeEnd = reader.ReadInt32();
            var textRangeOwner = AssetUtils.ReadOWORD(reader);
            var parameterTypesCount = reader.ReadInt32();
            var parameterTypes = new string[parameterTypesCount];
            for (var i = 0; i < parameterTypesCount; i++)
            {
                parameterTypes[i] = reader.ReadString();
            }
            var psiModuleName = reader.ReadString();
            var argumentNameRangeStart = reader.ReadInt32();
            var argumentNameRangeEnd = reader.ReadInt32();
            var targetType = reader.ReadString();

            return new BoltGraphMethodUsages(ownerName, methodName, new TextRange(textRangeStart, textRangeEnd), textRangeOwner, parameterTypes, psiModuleName, new TextRange
                (argumentNameRangeStart, argumentNameRangeEnd), targetType);
        }

        protected bool Equals(BoltGraphMethodUsages other)
        {
            return OwnerName == other.OwnerName &&
                   MethodName == other.MethodName && ArrayUtil.StructuralEquals(ParameterTypes, other.ParameterTypes) &&
                   PsiModuleName == other.PsiModuleName && ArgumentTypeNameRange == other.ArgumentTypeNameRange &&
                   Equals(TargetType, other.TargetType) && TextRangeOwnerPsiPersistentIndex.Equals(other.TextRangeOwnerPsiPersistentIndex) &&
                   TextRangeOwner == other.TextRangeOwner;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((BoltGraphMethodUsages) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = OwnerName.GetHashCode();
                hashCode = (hashCode * 397) ^ MethodName.GetHashCode();
                hashCode = (hashCode * 397) ^ (ParameterTypes != null ? ArrayUtil.StructuralGetHashCode(ParameterTypes) : 0);
                hashCode = (hashCode * 397) ^ TargetType.GetHashCode();
                hashCode = (hashCode * 397) ^ TextRangeOwnerPsiPersistentIndex.GetHashCode();
                hashCode = (hashCode * 397) ^ TextRangeOwner.GetHashCode();
                return hashCode;
            }
        }

        public Dictionary<string, IAssetValue> ToDictionary()
        {
            var dictionary = new Dictionary<string, IAssetValue>();
            return dictionary;
        }
    }
}