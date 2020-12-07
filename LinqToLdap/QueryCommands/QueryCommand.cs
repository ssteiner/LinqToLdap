﻿using LinqToLdap.Logging;
using LinqToLdap.Mapping;
using LinqToLdap.QueryCommands.Options;
using System;
using System.DirectoryServices.Protocols;
using System.Linq;

namespace LinqToLdap.QueryCommands
{
    internal abstract class QueryCommand : IQueryCommand
    {
        internal readonly SearchRequest SearchRequest;
        protected readonly IQueryCommandOptions Options;
        protected readonly IObjectMapping Mapping;

        protected QueryCommand(IQueryCommandOptions options, IObjectMapping mapping, bool initializeAttributes)
        {
            Options = options;
            Mapping = mapping;
            SearchRequest = new SearchRequest { Filter = options.Filter };
            if (Options.Controls != null)
            {
                SearchRequest.Controls.AddRange(Options.Controls.ToArray());
            }
            if (initializeAttributes)
            {
                InitializeAttributes();
            }
        }

        private void InitializeAttributes()
        {
            if (!Mapping.HasCatchAllMapping)
            {
                var attributes = Mapping.HasSubTypeMappings
                    ? Options.AttributesToLoad.Values
                        .Union(new[] { "objectClass" }, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : Options.AttributesToLoad.Values.ToArray();
                SearchRequest.Attributes.AddRange(attributes);
            }
        }

        protected virtual T GetControl<T>(DirectoryControl[] controls) where T : class
        {
            if (controls == null || controls.Length == 0) return default;

            return controls.FirstOrDefault(c => c is T) as T;
        }

        protected virtual T GetControl<T>(DirectoryControlCollection controls) where T : class
        {
            if (controls == null || controls.Count == 0) return default;

            return controls.OfType<T>().FirstOrDefault();
        }

        public abstract object Execute(DirectoryConnection connection, SearchScope scope, int maxPageSize, bool pagingEnabled, ILinqToLdapLogger log = null, string namingContext = null);

#if !NET35 && !NET40

        public abstract System.Threading.Tasks.Task<object> ExecuteAsync(LdapConnection connection, SearchScope scope, int maxPageSize, bool pagingEnabled, ILinqToLdapLogger log = null, string namingContext = null);

#endif

        protected void SetDistinguishedName(string namingContext)
        {
            SearchRequest.DistinguishedName = namingContext ?? Mapping.NamingContext;
        }

        public override string ToString()
        {
            return SearchRequest.ToLogString();
        }
    }
}