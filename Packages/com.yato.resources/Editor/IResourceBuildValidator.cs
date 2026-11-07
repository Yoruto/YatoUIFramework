using System;
using System.Collections.Generic;
using UnityEditor;

namespace Yato.Resources.Editor
{
    /// <summary>
    /// Optional Editor build validation. Implementations must have a public parameterless constructor.
    /// Optional build tools discover installed validators without referencing their packages.
    /// </summary>
    public interface IResourceBuildValidator
    {
        int Order { get; }
        void Validate();
    }

    public static class ResourceBuildValidation
    {
        public static void ValidateAll()
        {
            var validators = new List<IResourceBuildValidator>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IResourceBuildValidator>())
            {
                if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters) continue;
                validators.Add((IResourceBuildValidator)Activator.CreateInstance(type));
            }
            validators.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0 ? order : string.CompareOrdinal(left.GetType().FullName, right.GetType().FullName);
            });
            foreach (var validator in validators) validator.Validate();
        }
    }
}
