﻿using LinqToLdap.Logging;
using LinqToLdap.Mapping;
using LinqToLdap.QueryCommands.Options;
using System;
using System.DirectoryServices.Protocols;

namespace LinqToLdap.QueryCommands
{
    internal class SingleOrDefaultQueryCommand : QueryCommand
    {
        public SingleOrDefaultQueryCommand(IQueryCommandOptions options, IObjectMapping mapping)
            : base(options, mapping, true)
        {
        }

        public override object Execute(DirectoryConnection connection, SearchScope scope, int maxPageSize, bool pagingEnabled, ILinqToLdapLogger log = null, string namingContext = null)
        {
            if (Options.YieldNoResults) return Options.GetTransformer().Default();

            BuildRequest(scope, maxPageSize, pagingEnabled, log, namingContext);

            var response = connection.SendRequest(SearchRequest) as SearchResponse;

            return HandleResponse(response);
        }

#if !NET35 && !NET40

        public override async System.Threading.Tasks.Task<object> ExecuteAsync(LdapConnection connection, SearchScope scope, int maxPageSize, bool pagingEnabled, ILinqToLdapLogger log = null, string namingContext = null)
        {
            if (Options.YieldNoResults) return Options.GetTransformer().Default();

            BuildRequest(scope, maxPageSize, pagingEnabled, log, namingContext);

#if NET45
            return await System.Threading.Tasks.Task.Factory.FromAsync(
                (callback, state) =>
                {
                    return connection.BeginSendRequest(SearchRequest, Options.AsyncProcessing, callback, state);
                },
                (asyncresult) =>
                {
                    var response = (SearchResponse)connection.EndSendRequest(asyncresult);
                    return HandleResponse(response);
                },
                null
            ).ConfigureAwait(false);
#else
            var response = await System.Threading.Tasks.Task.Run(() => connection.SendRequest(SearchRequest) as SearchResponse).ConfigureAwait(false);
            return HandleResponse(response);
#endif
        }

#endif

        private void BuildRequest(SearchScope scope, int maxPageSize, bool pagingEnabled, ILinqToLdapLogger log = null, string namingContext = null)
        {
            SetDistinguishedName(namingContext);
            SearchRequest.Scope = scope;

            if (Options.SortingOptions != null)
            {
                if (GetControl<SortRequestControl>(SearchRequest.Controls) != null)
                    throw new InvalidOperationException("Only one sort request control can be sent to the server");

                SearchRequest.Controls.Add(new SortRequestControl(Options.SortingOptions.Keys) { IsCritical = false });
            }
            if (GetControl<PageResultRequestControl>(SearchRequest.Controls) != null)
            {
                throw new InvalidOperationException("Only one page request control can be sent to the server.");
            }

            if (pagingEnabled && !Options.WithoutPaging)
            {
                SearchRequest.Controls.Add(new PageResultRequestControl(2));
            }

            if (log != null && log.TraceEnabled) log.Trace(SearchRequest.ToLogString());
        }

        private object HandleResponse(SearchResponse response)
        {
            response.AssertSuccess();

            if (response.Entries.Count > 1)
            {
                throw new InvalidOperationException(string.Format("SingleOrDefault returned {0} results for {1}",
                                                                  response.Entries.Count, SearchRequest.Filter));
            }

            return response.Entries.Count == 1
                ? Options.GetTransformer().Transform(response.Entries[0])
                : Options.GetTransformer().Default();
        }
    }
}