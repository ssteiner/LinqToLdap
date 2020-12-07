using LinqToLdap.Collections;
using LinqToLdap.Exceptions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LinqToLdap.Mapping
{
    /// <summary>
    /// Uses attributes to map a class
    /// </summary>
    /// <typeparam name="T">The class to map</typeparam>
    public class OverrideAttributeClassMapper<T> : AttributeClassMap<T> where T : class
    {
        private Dictionary<string, string> myDict;

        /// <summary>
        /// instantiates a new mapper
        /// </summary>
        /// <param name="dictionary"></param>
        public OverrideAttributeClassMapper(Dictionary<string, string> dictionary)
        {
            myDict = dictionary ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Maps class information for <typeparamref name="T"/>.
        /// </summary>
        /// <exception cref="MappingException">Thrown if <typeparamref name="T"/> does not have a <see cref="DirectorySchemaAttribute"/>.</exception>
        public override IClassMap PerformMapping(string namingContext = null, string objectCategory = null, bool includeObjectCategory = true, IEnumerable<string> objectClasses = null, bool includeObjectClasses = true)
        {
            var type = typeof(T);
            var schemaAttribute = type
                .GetCustomAttributes(typeof(DirectorySchemaAttribute), true)
                .Cast<DirectorySchemaAttribute>()
                .FirstOrDefault();

            if (schemaAttribute == null)
            {
                throw new MappingException("DirectorySchemaAttribute not found for " + typeof(T).FullName);
            }

            WithoutSubTypeMapping = schemaAttribute.WithoutSubTypeMapping;

            NamingContext(namingContext.IsNullOrEmpty() ? schemaAttribute.NamingContext : namingContext);
            if (!objectCategory.IsNullOrEmpty())
            {
                ObjectCategory(objectCategory, includeObjectCategory);
            }
            else
            {
                ObjectCategory(schemaAttribute.ObjectCategory, schemaAttribute.IncludeObjectCategory);
            }

            if (objectClasses != null)
            {
                ObjectClasses(objectClasses, includeObjectClasses);
            }
            else
            {
                ObjectClasses(schemaAttribute.ObjectClasses, schemaAttribute.IncludeObjectClasses);
            }

            var allProperties = type.GetProperties(Flags)
                .Where(p => p.GetGetMethod(true) != null && p.GetSetMethod(true) != null)
                .ToList();

            var properties = allProperties
                .Where(p => p.GetCustomAttributes(typeof(DirectoryAttributeAttribute), true).Any())
                .Select(p =>
                        new KeyValuePair<DirectoryAttributeAttribute, PropertyInfo>(
                            p.GetCustomAttributes(typeof(DirectoryAttributeAttribute), true)
                                .Cast<DirectoryAttributeAttribute>()
                                .FirstOrDefault(),
                            p));

            properties.ToList()
                .ForEach(p =>
                {
                    var attributeName = p.Key.AttributeName;
                    if (myDict.ContainsKey(p.Value.Name))
                        attributeName = myDict[p.Value.Name];
                    var property = MapPropertyInfo(p.Value)
                        .Named(attributeName);

                    if (p.Key.EnumStoredAsInt)
                        property.EnumStoredAsInt();

                    if (p.Key.ReadOnlyOnAdd && p.Key.ReadOnlyOnSet)
                        property.ReadOnly(ReadOnly.Always);
                    else if (p.Key.ReadOnlyOnAdd)
                        property.ReadOnly(ReadOnly.OnAdd);
                    else if (p.Key.ReadOnlyOnSet)
                        property.ReadOnly(ReadOnly.OnUpdate);

                    property.DateTimeFormat(p.Key.DateTimeFormat);
                });

            var distinguishedName = allProperties
                .Where(p => p.GetCustomAttributes(typeof(DistinguishedNameAttribute), true).Any())
#if NET35
                .Select(p => new LinqToLdap.Helpers.TwoTuple<PropertyInfo, DistinguishedNameAttribute>(p,
#else
                .Select(p => new System.Tuple<PropertyInfo, DistinguishedNameAttribute>(p,
#endif

                    p.GetCustomAttributes(typeof(DistinguishedNameAttribute), true).Cast<DistinguishedNameAttribute>().First()))
                .FirstOrDefault();

            if (distinguishedName != null)
            {
                DistinguishedName(distinguishedName.Item1, distinguishedName.Item2.AttributeName);
            }

            var catchAll = allProperties
                .FirstOrDefault(p => typeof(IDirectoryAttributes).IsAssignableFrom(p.PropertyType));

            if (catchAll != null)
            {
                CatchAll(catchAll);
            }

            return this;
        }
    }
}