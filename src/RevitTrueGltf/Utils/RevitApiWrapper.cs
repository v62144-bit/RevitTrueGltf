using Autodesk.Revit.DB;

namespace RevitTrueGltf.Utils
{
    /// <summary>
    /// Bridge for Revit API differences between different versions (2020-2027).
    /// Centralizes all #if preprocessor directives to keep business logic clean.
    /// </summary>
    public static class RevitApiWrapper
    {
        public static long GetIdValue(this ElementId id)
        {
            if (id == null) return -1;
#if REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        public static ElementId GetSymbolId(this InstanceNode node)
        {
            if (node == null) return ElementId.InvalidElementId;
#if REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
            return node.GetSymbolGeometryId().SymbolId;
#else
            return node.GetSymbolId();
#endif
        }

        public static string GetCategoryKey(Category category)
        {
            if (category == null) return "None";
#if REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
            return ((BuiltInCategory)(int)category.Id.Value).ToString();
#else
            return ((BuiltInCategory)category.Id.IntegerValue).ToString();
#endif
        }

        public static string GetParameterGroupKey(Definition definition)
        {
#if REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
            return definition.GetGroupTypeId()?.TypeId ?? "";
#else
            return definition.ParameterGroup.ToString();
#endif
        }

        public static string GetParameterGroupName(Definition definition)
        {
#if REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
            var typeId = definition.GetGroupTypeId();
            return typeId != null ? LabelUtils.GetLabelForGroup(typeId) : "";
#else
            return LabelUtils.GetLabelFor(definition.ParameterGroup);
#endif
        }

        public static string GetParameterUnitType(Definition definition)
        {
            if (definition == null) return "None";
#if REVIT2023 || REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
            return definition.GetDataType()?.TypeId ?? "None";
#elif REVIT2021 || REVIT2022
            return definition.GetSpecTypeId()?.TypeId ?? "None";
#else
            try {
                return definition.UnitType.ToString();
            } catch { return "None"; }
#endif
        }
    }
}
