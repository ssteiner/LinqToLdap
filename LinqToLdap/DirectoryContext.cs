﻿#if !NET35 && !NET40

using LinqToLdap.Async;
using System.Threading.Tasks;

#endif

using LinqToLdap.Collections;
using LinqToLdap.EventListeners;
using LinqToLdap.Exceptions;
using LinqToLdap.Logging;
using LinqToLdap.Mapping;
using LinqToLdap.Transformers;
using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.IO;
using System.Linq;

namespace LinqToLdap
{
    /// <summary>
    /// Implementation for performing LINQ queries against a directory
    /// </summary>
    public class DirectoryContext : IDirectoryContext
    {
        private bool _disposed;
        private readonly bool _disposeOfConnection = true;
        private readonly bool _connectionIsFromFactory;
        private LdapConnection _connection;
        private ILdapConfiguration _configuration;

        /// <summary>
        /// Creates an instance and uses the specified <paramref name="connection"/> for querying.
        /// The connection will not be disposed of when this instance is disposed.
        /// </summary>
        /// <param name="connection">Connection to use</param>
        /// <param name="disposeOfConnection">Indicates if the context should dispose of the connection when <see cref="Dispose()"/> is called.</param>
        /// <param name="configuration">The configuration.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="connection"/> is null</exception>
        public DirectoryContext(LdapConnection connection, bool disposeOfConnection = false, ILdapConfiguration configuration = null)
        {
            if (configuration == null)
            {
                configuration = LdapConfiguration.Configuration ?? new LdapConfiguration();
            }

            _configuration = configuration;
            Logger = _configuration.Log;
            _disposeOfConnection = disposeOfConnection;
            _connection = connection ?? throw new ArgumentNullException("connection");
        }

        /// <summary>
        /// Indicates if this object has been disposed
        /// </summary>
        public bool IsDisposed { get => _disposed; }

        /// <summary>
        /// Creates an instance for querying.
        /// The underlying connection will be disposed of when this instance is disposed if the <see cref="LdapConfiguration"/> doeas not
        /// have a configured <see cref="ILdapConnectionFactory"/>.  Otherwise <see cref="ILdapConnectionFactory.ReleaseConnection"/> will be called.
        /// </summary>
        /// <exception cref="MappingException">
        /// Thrown if <paramref name="configuration"/> or <see cref="ILdapConfiguration.ConnectionFactory"/> is null.
        /// </exception>
        /// <param name="configuration">The configuration.</param>
        public DirectoryContext(ILdapConfiguration configuration)
        {
            if (configuration == null || configuration.ConnectionFactory == null)
                throw new MappingException("configuration cannot be null and must be associated with a connection factory.");

            _configuration = configuration;
            _disposeOfConnection = true;
            Logger = _configuration.Log;
            _connection = _configuration.ConnectionFactory.GetConnection();
            _connectionIsFromFactory = true;
        }

        /// <summary>
        /// Creates an instance for querying.
        /// The underlying connection will be disposed of when this instance is disposed if the <see cref="LdapConfiguration"/> doeas not
        /// have a configured <see cref="ILdapConnectionFactory"/>.  Otherwise <see cref="ILdapConnectionFactory.ReleaseConnection"/> will be called.
        /// </summary>
        /// <exception cref="MappingException">Thrown if <see cref="LdapConfiguration"/> has not been initialized.</exception>
        public DirectoryContext()
        {
            if (LdapConfiguration.Configuration == null || LdapConfiguration.Configuration.ConnectionFactory == null)
                throw new MappingException("A static configuration and connection factory must be provided. See LdapConfiguration.UseStaticStorage()");

            _disposeOfConnection = true;
            _configuration = LdapConfiguration.Configuration;
            Logger = _configuration.Log;
            _connection = _configuration.ConnectionFactory.GetConnection();
            _connectionIsFromFactory = true;
        }

        /// <summary>
        /// Allows for logging filters and errors.
        /// </summary>
        public ILinqToLdapLogger Logger { private get; set; }

        /// <summary>
        /// Allows for logging filters and errors.
        /// </summary>
        public TextWriter Log
        {
            set
            {
                if (value != null)
                {
                    Logger = new SimpleTextLogger(value);
                }
            }
        }

        #region Query Methods

        /// <summary>
        /// Creates a query against the directory.
        /// </summary>
        /// <typeparam name="T">Directory type</typeparam>
        /// <param name="scope">Determines the depth at which the search is performed</param>
        /// <param name="namingContext">Optional naming context to override the mapped naming context.</param>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public IQueryable<T> Query<T>(SearchScope scope = SearchScope.Subtree, string namingContext = null) where T : class
        {
            return PrivateQuery<T>(scope, namingContext);
        }

        private IQueryable<T> PrivateQuery<T>(SearchScope scope, string namingContext, string objectClass = null, IEnumerable<string> objectClasses = null, string objectCategory = null) where T : class
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                var mapping = _configuration.Mapper.Map<T>(namingContext, objectClass, objectClasses, objectCategory);
                var provider = new DirectoryQueryProvider(_connection, scope, mapping, _configuration.PagingEnabled)
                {
                    Log = Logger,
                    MaxPageSize = _configuration.ServerMaxPageSize,
                    NamingContext = namingContext,
                    MaxResultSize = _configuration.ServerMaxResultSize
                };
                var directoryQuery = new DirectoryQuery<T>(provider);
                return directoryQuery;
            }
            catch (Exception ex)
            {
                if (Logger != null) Logger.Error(ex);
                throw;
            }
        }

        /// <summary>
        /// Creates a query against the directory.
        /// </summary>
        /// <typeparam name="T">Directory type</typeparam>
        /// <param name="example">An anonymous object that can be used for auto mapping.</param>
        /// <param name="namingContext">The place in the directory from which you want to start your search.</param>
        /// <param name="objectClass">The object class in the directory for the type.</param>
        /// <param name="objectClasses">The object classes in the directory for the type.</param>
        /// <param name="objectCategory">The object category in the directory for the type</param>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public IQueryable<T> Query<T>(T example, string namingContext, string objectClass = null, IEnumerable<string> objectClasses = null, string objectCategory = null) where T : class
        {
            return PrivateQuery<T>(SearchScope.Subtree, namingContext, objectClass, objectClasses, objectCategory);
        }

        /// <summary>
        /// Creates a query against the directory.
        /// </summary>
        /// <typeparam name="T">Directory type</typeparam>
        /// <param name="scope">Determines the depth at which the search is performed</param>
        /// <param name="example">An anonymous object that can be used for auto mapping.</param>
        /// <param name="namingContext">The place in the directory from which you want to start your search.</param>
        /// <param name="objectClass">The object class in the directory for the type.</param>
        /// <param name="objectClasses">The object classes in the directory for the type.</param>
        /// <param name="objectCategory">The object category in the directory for the type</param>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public IQueryable<T> Query<T>(T example, SearchScope scope, string namingContext, string objectClass = null, IEnumerable<string> objectClasses = null, string objectCategory = null) where T : class
        {
            return PrivateQuery<T>(scope, namingContext, objectClass, objectClasses, objectCategory);
        }

        /// <summary>
        /// Creates a query against the directory for a dynamic type.
        /// </summary>
        /// <param name="namingContext">The place in the directory from which you want to start your search.</param>
        /// <param name="scope">Determines the depth at which the search is performed</param>
        /// <param name="objectClass">The object class in the directory for the type.</param>
        /// <param name="objectClasses">The object classes in the directory for the type.</param>
        /// <param name="objectCategory">The object category in the directory for the type</param>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public IQueryable<IDirectoryAttributes> Query(string namingContext, SearchScope scope = SearchScope.Subtree, string objectClass = null,
            IEnumerable<string> objectClasses = null, string objectCategory = null)
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                var mapping = new DynamicObjectMapping(namingContext, objectClasses, objectCategory, objectClass);
                var provider = new DirectoryQueryProvider(_connection, scope, mapping, _configuration.PagingEnabled)
                {
                    Log = Logger,
                    IsDynamic = true,
                    MaxPageSize = _configuration.ServerMaxPageSize
                };
                var directoryQuery = new DirectoryQuery<IDirectoryAttributes>(provider);
                return directoryQuery;
            }
            catch (Exception ex)
            {
                if (Logger != null) Logger.Error(ex);

                throw;
            }
        }

        #endregion Query Methods

        /// <summary>
        /// List server information from RootDSE.
        /// </summary>
        /// <param name="attributes">
        /// Specify specific attributes to load.  Some LDAP servers require an explicit request for certain attributes.
        /// </param>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public IDirectoryAttributes ListServerAttributes(params string[] attributes)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return _connection.ListServerAttributes(attributes, Logger);
        }

#if !NET35 && !NET40

        /// <summary>
        /// List server information from RootDSE.
        /// </summary>
        /// <param name="attributes">
        /// Specify specific attributes to load.  Some LDAP servers require an explicit request for certain attributes.
        /// </param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public async Task<IDirectoryAttributes> ListServerAttributesAsync(string[] attributes = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return await LdapConnectionAsyncExtensions.ListServerAttributesAsync(_connection, attributes, Logger, resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves the attributes from the directory using the distinguished name.  <see cref="SearchScope.Base"/> is used.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name to look for.</param>
        /// <param name="attributes">The attributes to load.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public async Task<IDirectoryAttributes> GetByDNAsync(string distinguishedName, string[] attributes = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return await LdapConnectionAsyncExtensions.GetByDNAsync(_connection, distinguishedName, Logger, attributes, resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves the mapped class from the directory using the distinguished name.  <see cref="SearchScope.Base"/> is used.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name to look for.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <typeparam name="T">The type of mapped object</typeparam>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public async Task<T> GetByDNAsync<T>(string distinguishedName, PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing) where T : class
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                var mapping = _configuration.Mapper.Map<T>();

                var request = new SearchRequest { DistinguishedName = distinguishedName, Scope = SearchScope.Base };

                var attributes = mapping.HasCatchAllMapping
                    ? new string[0]
                    : (mapping.HasSubTypeMappings
                        ? mapping.Properties.Values.Union(new[] { "objectClass" }, StringComparer.OrdinalIgnoreCase)
                        : mapping.Properties.Values);

                foreach (var property in attributes)
                {
                    request.Attributes.Add(property);
                }

                var transformer = new ResultTransformer(mapping.Properties, mapping);

                if (Logger != null && Logger.TraceEnabled) Logger.Trace(request.ToLogString());

                SearchResponse response;
#if NET45
                return await Task.Factory.FromAsync(
                    (callback, state) =>
                    {
                        return _connection.BeginSendRequest(request, resultProcessing, callback, state);
                    },
                    (asyncresult) =>
                    {
                        response = (SearchResponse)_connection.EndSendRequest(asyncresult);
                        response.AssertSuccess();

                        var entry = (response.Entries.Count == 0
                                ? transformer.Default()
                                : transformer.Transform(response.Entries[0])) as T;

                        return entry;
                    },
                    null
                ).ConfigureAwait(false);
#else
                response = await Task.Run(() => _connection.SendRequest(request) as SearchResponse).ConfigureAwait(false);
                response.AssertSuccess();
                var entry = (response.Entries.Count == 0
                                ? transformer.Default()
                                : transformer.Transform(response.Entries[0])) as T;

                return entry;
#endif
            }
            catch (Exception ex)
            {
                if (Logger != null) Logger.Error(ex, string.Format("An error occurred while trying to retrieve '{0}'.", distinguishedName));

                throw;
            }
        }

#endif

        /// <summary>
        /// Retrieves the attributes from the directory using the distinguished name.  <see cref="SearchScope.Base"/> is used.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name to look for.</param>
        /// <param name="attributes">The attributes to load.</param>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public IDirectoryAttributes GetByDN(string distinguishedName, params string[] attributes)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return _connection.GetByDN(distinguishedName, Logger, attributes);
        }

        /// <summary>
        /// Retrieves the mapped class from the directory using the distinguished name.  <see cref="SearchScope.Base"/> is used.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name to look for.</param>
        /// <typeparam name="T">The type of mapped object</typeparam>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public T GetByDN<T>(string distinguishedName) where T : class
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                var mapping = _configuration.Mapper.Map<T>();

                var request = new SearchRequest { DistinguishedName = distinguishedName, Scope = SearchScope.Base };

                var attributes = mapping.HasCatchAllMapping
                    ? new string[0]
                    : (mapping.HasSubTypeMappings
                        ? mapping.Properties.Values.Union(new[] { "objectClass" }, StringComparer.OrdinalIgnoreCase)
                        : mapping.Properties.Values);

                foreach (var property in attributes)
                {
                    request.Attributes.Add(property);
                }

                var transformer = new ResultTransformer(mapping.Properties, mapping);

                if (Logger != null && Logger.TraceEnabled) Logger.Trace(request.ToLogString());

                var response = _connection.SendRequest(request) as SearchResponse;
                response.AssertSuccess();

                // ReSharper disable PossibleNullReferenceException
                var entry = (response.Entries.Count == 0
                        ? transformer.Default()
                        : transformer.Transform(response.Entries[0])) as T;
                // ReSharper restore PossibleNullReferenceException

                return entry;
            }
            catch (Exception ex)
            {
                if (Logger != null) Logger.Error(ex, string.Format("An error occurred while trying to retrieve '{0}'.", distinguishedName));

                throw;
            }
        }

#if !NET35 && !NET40

        /// <summary>
        /// Adds the entry to the directory and returns the newly saved entry from the directory. If the <paramref name="distinguishedName"/> is
        /// null then a mapped distinguished name property is used.
        /// </summary>
        /// <typeparam name="T">The type of entry.</typeparam>
        /// <param name="entry">The object to save</param>
        /// <param name="distinguishedName">The distinguished name for the entry. Ignored if <typeparamref name="T"/> is an instance of <see cref="IDirectoryAttributes"/></param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if entry is null</exception>
        /// <exception cref="ArgumentException">Thrown if distinguished name is null and there is no mapped distinguished name property.</exception>
        /// <exception cref="MappingException">
        /// Thrown if <paramref name="distinguishedName"/> is null and Distinguished Name is not mapped.
        /// Thrown if object class or object category have not been mapped.
        /// Thrown if <typeparamref name="T"/> has not been mapped.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the add was not successful.</exception>
        /// <exception cref="LdapException">Thrown if the add was not successful.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task<T> AddAndGetAsync<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing) where T : class
        {
            if (entry is IDirectoryAttributes x)
            {
                return (await AddAndGetEntryAsync(x, controls, resultProcessing).ConfigureAwait(false)) as T;
            }
            else
            {
                var dn = await AddEntryAsync(entry, distinguishedName, controls).ConfigureAwait(false);

                return await GetByDNAsync<T>(dn).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adds the entry to the directory. If the <paramref name="distinguishedName"/> is
        /// null then a mapped distinguished name property is used.
        /// </summary>
        /// <typeparam name="T">The type of entry.</typeparam>
        /// <param name="entry">The object to save.</param>
        /// <param name="distinguishedName">The distinguished name for the entry. Ignored if <typeparamref name="T"/> is an instance of <see cref="IDirectoryAttributes"/></param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if entry is null</exception>
        /// <exception cref="ArgumentException">Thrown if distinguished name is null and there is no mapped distinguished name property.</exception>
        /// <exception cref="MappingException">
        /// Thrown if <paramref name="distinguishedName"/> is null and Distinguished Name is not mapped.
        /// Thrown if object class or object category have not been mapped.
        /// Thrown if <typeparamref name="T"/> has not been mapped.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the add was not successful.</exception>
        /// <exception cref="LdapException">Thrown if the add was not successful.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task AddAsync<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing) where T : class
        {
            if (entry is IDirectoryAttributes x) await AddEntryAsync(x, controls, resultProcessing).ConfigureAwait(false);
            else await AddEntryAsync(entry, distinguishedName, controls, resultProcessing).ConfigureAwait(false);
        }

#endif

        /// <summary>
        /// Adds the entry to the directory and returns the newly saved entry from the directory. If the <paramref name="distinguishedName"/> is
        /// null then a mapped distinguished name property is used.
        /// </summary>
        /// <typeparam name="T">The type of entry.</typeparam>
        /// <param name="entry">The object to save</param>
        /// <param name="distinguishedName">The distinguished name for the entry. Ignored if <typeparamref name="T"/> is an instance of <see cref="IDirectoryAttributes"/></param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if entry is null</exception>
        /// <exception cref="ArgumentException">Thrown if distinguished name is null and there is no mapped distinguished name property.</exception>
        /// <exception cref="MappingException">
        /// Thrown if <paramref name="distinguishedName"/> is null and Distinguished Name is not mapped.
        /// Thrown if object class or object category have not been mapped.
        /// Thrown if <typeparamref name="T"/> has not been mapped.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the add was not successful.</exception>
        /// <exception cref="LdapException">Thrown if the add was not successful.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public T AddAndGet<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null) where T : class
        {
            if (entry is IDirectoryAttributes x)
            {
                return AddAndGetEntry(x, controls) as T;
            }
            else
            {
                var dn = AddEntry(entry, distinguishedName, controls);

                return GetByDN<T>(dn);
            }
        }

        /// <summary>
        /// Adds the entry to the directory. If the <paramref name="distinguishedName"/> is
        /// null then a mapped distinguished name property is used.
        /// </summary>
        /// <typeparam name="T">The type of entry.</typeparam>
        /// <param name="entry">The object to save.</param>
        /// <param name="distinguishedName">The distinguished name for the entry. Ignored if <typeparamref name="T"/> is an instance of <see cref="IDirectoryAttributes"/></param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if entry is null</exception>
        /// <exception cref="ArgumentException">Thrown if distinguished name is null and there is no mapped distinguished name property.</exception>
        /// <exception cref="MappingException">
        /// Thrown if <paramref name="distinguishedName"/> is null and Distinguished Name is not mapped.
        /// Thrown if object class or object category have not been mapped.
        /// Thrown if <typeparamref name="T"/> has not been mapped.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the add was not successful.</exception>
        /// <exception cref="LdapException">Thrown if the add was not successful.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public void Add<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null) where T : class
        {
            if (entry is IDirectoryAttributes x) AddEntry(x, controls);
            else AddEntry(entry, distinguishedName, controls);
        }

        private string AddEntry<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null) where T : class
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                if (entry == null) throw new ArgumentNullException(nameof(entry));
                var objectMapping = _configuration.Mapper.GetMapping(entry.GetType());
                if (objectMapping == null) throw new MappingException("Cannot add an unmapped class.");

                var attributes = new List<DirectoryAttribute>();

                if (objectMapping.ObjectClasses != null && objectMapping.ObjectClasses.Any())
                {
                    attributes.Add(new DirectoryAttribute("objectClass", objectMapping.ObjectClasses.Select(oc => (object)oc).ToArray()));
                }
                else
                {
                    throw new MappingException(
                        $"Cannot add an entry without mapping objectClass for {typeof(T).FullName}.");
                }

                distinguishedName = GetDistinguishedName(distinguishedName, objectMapping, entry);

                var request = new AddRequest(distinguishedName);

                if (controls != null)
                {
                    request.Controls.AddRange(controls);
                }

                var directoryAttributes = objectMapping.GetPropertyMappingsForAdd()
                    .Select(pm => pm.GetDirectoryAttribute(entry))
                    .Where(da => da.Count > 0);

                var catchAll =
                    objectMapping.GetCatchAllMapping()?.GetValue(entry) as IDirectoryAttributes;

                catchAll?.GetChangedAttributes().Where(da => da.Count > 0).ForEach(x => attributes.Add(x));

                foreach (var da in directoryAttributes.Union(attributes))
                {
                    request.Attributes.Add(da);
                }

                var preArgs = new ListenerPreArgs<object, AddRequest>(entry, request, _connection);
                foreach (var eventListener in _configuration.GetListeners<IPreAddEventListener>())
                {
                    eventListener.Notify(preArgs);
                }

                if (Logger != null && Logger.TraceEnabled) Logger.Trace(request.ToLogString());

                var response = _connection.SendRequest(request) as AddResponse;
                response.AssertSuccess();

                var postArgs = new ListenerPostArgs<object, AddRequest, AddResponse>(entry, request, response, _connection);
                foreach (var eventListener in _configuration.GetListeners<IPostAddEventListener>())
                {
                    eventListener.Notify(postArgs);
                }
            }
            catch (Exception ex)
            {
                if (Logger != null) Logger.Error(ex, string.Format("An error occurred while trying to add '{0}'.", distinguishedName));
                throw;
            }

            return distinguishedName;
        }

        /// <summary>
        /// Adds the entry to the directory.
        /// </summary>
        /// <param name="entry">The attributes for the entry</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="entry"/> is null.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the add was not successful.</exception>
        /// <exception cref="LdapException">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public void AddEntry(IDirectoryAttributes entry, DirectoryControl[] controls = null)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            _connection.Add(entry, Logger, controls, _configuration.GetListeners<IAddEventListener>());
        }

        /// <summary>
        /// Adds the entry to the directory and returns the newly saved entry from the directory.
        /// </summary>
        /// <param name="entry">The attributes for the entry</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="entry"/> is null.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the add was not successful.</exception>
        /// <exception cref="LdapException">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public IDirectoryAttributes AddAndGetEntry(IDirectoryAttributes entry, DirectoryControl[] controls = null)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            return _connection.AddAndGet(entry, Logger, controls, _configuration.GetListeners<IAddEventListener>());
        }

        /// <summary>
        /// Deletes an entry from the directory.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name of the entry</param><param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="distinguishedName"/> is null, empty or white space.</exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapException">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public void Delete(string distinguishedName, params DirectoryControl[] controls)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            _connection.Delete(distinguishedName, Logger, controls, _configuration.GetListeners<IDeleteEventListener>());
        }

        /// <summary>
        /// Updates the entry in the directory. If the <paramref name="distinguishedName"/> is
        /// null then a mapped distinguished name property is used.
        /// </summary>
        /// <param name="entry">The entry to update</param>
        /// <param name="distinguishedName">The distinguished name for the entry. Ignored if <typeparamref name="T"/> is an instance of <see cref="IDirectoryAttributes"/></param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <typeparam name="T">The type of entry.</typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if entry is null</exception>
        /// <exception cref="MappingException">
        /// Thrown if <paramref name="distinguishedName"/> is null and Distinguished Name is not mapped.
        /// Thrown if object class or object category have not been mapped.
        /// Thrown if <typeparamref name="T"/> has not been mapped.
        /// </exception>
        /// <exception cref="ArgumentException">Thrown if distinguished name is null and there is no mapped distinguished name property.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <paramref name="entry"/> is <see cref="DirectoryObjectBase"/> but the entry is not tracking changes.</exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation is not successful</exception>
        /// <exception cref="LdapException">Thrown if the operation is not successful</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public void Update<T>(T entry, string distinguishedName = null, params DirectoryControl[] controls) where T : class
        {
            if (entry is IDirectoryAttributes x) UpdateEntry(x, controls);
            else UpdateEntry(entry, distinguishedName, controls);
        }

        /// <summary>
        /// Updates the entry in the directory and returns the updated version from the directory. If the <paramref name="distinguishedName"/> is
        /// null then a mapped distinguished name property is used.
        /// </summary>
        /// <param name="entry">The entry to update</param>
        /// <param name="distinguishedName">The distinguished name for the entry. Ignored if <typeparamref name="T"/> is an instance of <see cref="IDirectoryAttributes"/></param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <typeparam name="T">The type of entry.</typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if entry is null</exception>
        /// <exception cref="MappingException">
        /// Thrown if <paramref name="distinguishedName"/> is null and Distinguished Name is not mapped.
        /// Thrown if <typeparamref name="T"/> has not been mapped.
        /// </exception>
        /// <exception cref="ArgumentException">Thrown if distinguished name is null and there is no mapped distinguished name property.</exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation is not successful</exception>
        /// <exception cref="LdapException">Thrown if the operation is not successful</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public T UpdateAndGet<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null) where T : class
        {
            if (entry is IDirectoryAttributes x)
            {
                return UpdateAndGetEntry(x, controls) as T;
            }
            else
            {
                var dn = UpdateEntry(entry, distinguishedName, controls);

                return GetByDN<T>(dn);
            }
        }

        /// <summary>
        /// Updates the entry in the directory and returns the updated version from the directory.
        /// </summary>
        /// <param name="entry">The entry to update.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="entry"/> is null.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails</exception>
        /// <exception cref="LdapException">Thrown if the operation fails</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public IDirectoryAttributes UpdateAndGetEntry(IDirectoryAttributes entry, DirectoryControl[] controls = null)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return _connection.UpdateAndGet(entry, Logger, controls, _configuration.GetListeners<IUpdateEventListener>());
        }

        /// <summary>
        /// Updates the entry in the directory.
        /// </summary>
        /// <param name="entry">The entry to update.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="entry"/> is null.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails</exception>
        /// <exception cref="LdapException">Thrown if the operation fails</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public void UpdateEntry(IDirectoryAttributes entry, DirectoryControl[] controls = null)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            _connection.Update(entry, Logger, controls, _configuration.GetListeners<IUpdateEventListener>());
        }

        /// <summary>
        /// Adds the attribute to an entry.
        /// </summary>
        /// <param name="distinguishedName">The entry</param>
        /// <param name="attributeName">The name of the attribute</param>
        /// <param name="value">The value for the entry.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapConnection">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public void AddAttribute(string distinguishedName, string attributeName, object value = null, DirectoryControl[] controls = null)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            if (distinguishedName.IsNullOrEmpty())
                throw new ArgumentNullException("distinguishedName");

            var attributes = new DirectoryAttributes(distinguishedName);

            attributes.AddModification(value.ToDirectoryModification(attributeName, DirectoryAttributeOperation.Add));

            _connection.Update(attributes, Logger, controls, _configuration.GetListeners<IUpdateEventListener>());
        }

        /// <summary>
        /// Removes the attribute from an entry.
        /// </summary>
        /// <param name="distinguishedName">The entry</param>
        /// <param name="attributeName">The name of the attribute</param>
        /// <param name="value">The optional value. If null the whole attribute will be removed, otherwise the value will be removed.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="distinguishedName"/> is null, empty or white space.</exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapConnection">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public void DeleteAttribute(string distinguishedName, string attributeName, object value = null, DirectoryControl[] controls = null)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            if (distinguishedName.IsNullOrEmpty())
                throw new ArgumentNullException("distinguishedName");

            var attributes = new DirectoryAttributes(distinguishedName);

            attributes.AddModification(value.ToDirectoryModification(attributeName, DirectoryAttributeOperation.Delete));

            _connection.Update(attributes, Logger, controls, _configuration.GetListeners<IUpdateEventListener>());
        }

        /// <summary>
        /// Moves the entry from one container to another without modifying the entry's name.
        /// </summary>
        /// <param name="currentDistinguishedName">The entry's current distinguished name</param>
        /// <param name="newNamingContext">The new container for the entry</param>
        /// <param name="deleteOldRDN">Maps to <see cref="P:System.DirectoryServices.Protocols.ModifyDNRequest.DeleteOldRdn"/>. Defaults to null to use default behavior from <see cref="P:System.DirectoryServices.Protocols.ModifyDNRequest.DeleteOldRdn"/>.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="currentDistinguishedName"/> has an invalid format.
        /// </exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="currentDistinguishedName"/>
        /// or <paramref name="newNamingContext"/> are null, empty or white space.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapConnection">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public string MoveEntry(string currentDistinguishedName, string newNamingContext, bool? deleteOldRDN = null, params DirectoryControl[] controls)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return _connection.MoveEntry(currentDistinguishedName, newNamingContext, Logger, deleteOldRDN, controls);
        }

        /// <summary>
        /// Renames the entry within the same container. The <paramref name="newName"/> can be in the format
        /// XX=New Name or just New Name.
        /// </summary>
        /// <param name="currentDistinguishedName">The entry's current distinguished name</param>
        /// <param name="newName">The new name of the entry</param>
        /// <param name="deleteOldRDN">Maps to <see cref="P:System.DirectoryServices.Protocols.ModifyDNRequest.DeleteOldRdn"/>. Defaults to null to use default behavior from <see cref="P:System.DirectoryServices.Protocols.ModifyDNRequest.DeleteOldRdn"/>.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="currentDistinguishedName"/> has an invalid format.
        /// </exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="currentDistinguishedName"/>
        /// or <paramref name="newName"/> are null, empty or white space.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapConnection">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public string RenameEntry(string currentDistinguishedName, string newName, bool? deleteOldRDN = null, params DirectoryControl[] controls)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return _connection.RenameEntry(currentDistinguishedName, newName, Logger, deleteOldRDN, controls);
        }

        /// <summary>
        /// Uses range retrieval to get all values for <paramref name="attributeName"/> on <paramref name="distinguishedName"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the attribute.  Must be <see cref="string"/> or <see cref="Array"/> of <see cref="byte"/>.</typeparam>
        /// <param name="distinguishedName">The distinguished name of the entry.</param>
        /// <param name="attributeName">The attribute to load.</param>
        /// <param name="start">The starting point for the range. Defaults to 0.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="distinguishedName"/> or <paramref name="attributeName"/> is null, empty or white space.
        /// </exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public IList<TValue> RetrieveRanges<TValue>(string distinguishedName, string attributeName, int start = 0)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            return _connection.RetrieveRanges<TValue>(distinguishedName, attributeName, start, Logger);
        }

        /// <summary>
        /// Sends the request to the directory.
        /// </summary>
        /// <param name="request">The response from the directory</param>
        /// <returns></returns>
        public DirectoryResponse SendRequest(DirectoryRequest request)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            return _connection.SendRequest(request);
        }

        private string UpdateEntry<T>(T entry, string distinguishedName = null, params DirectoryControl[] controls)
            where T : class
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                if (entry == null) throw new ArgumentNullException(nameof(entry));

                var objectMapping = _configuration.Mapper.GetMapping(entry.GetType());
                if (objectMapping == null) throw new MappingException("Cannot update an unmapped class.");

                distinguishedName = GetDistinguishedName(distinguishedName, objectMapping, entry);

                var request = new ModifyRequest(distinguishedName);
                if (controls != null)
                {
                    request.Controls.AddRange(controls);
                }

                var modifications = new List<DirectoryAttributeModification>();

                if (!(entry is IDirectoryObject directoryObject))
                {
                    modifications.AddRange(objectMapping.GetPropertyMappingsForUpdate()
                        .Select(mapping => mapping.GetDirectoryAttributeModification(entry)));
                }
                else
                {
                    var changes = directoryObject.GetChanges(objectMapping);
                    modifications.AddRange(changes);
                }

                if (objectMapping.GetCatchAllMapping()?.GetValue(entry) is IDirectoryAttributes catchAll)
                {
                    modifications.AddRange(catchAll.GetChangedAttributes());
                }

                if (modifications.Count == 0)
                {
                    if (Logger != null && Logger.TraceEnabled) Logger.Trace(string.Format("No changes found for {0}.", distinguishedName));

                    return distinguishedName;
                }

                request.Modifications.AddRange(modifications.ToArray());

                var preArgs = new ListenerPreArgs<object, ModifyRequest>(entry, request, _connection);
                foreach (var eventListener in _configuration.GetListeners<IPreUpdateEventListener>())
                {
                    eventListener.Notify(preArgs);
                }

                if (Logger != null && Logger.TraceEnabled) Logger.Trace(request.ToLogString());

                var response = _connection.SendRequest(request) as ModifyResponse;
                response.AssertSuccess();

                var postArgs = new ListenerPostArgs<object, ModifyRequest, ModifyResponse>(entry, request, response, _connection);
                foreach (var eventListener in _configuration.GetListeners<IPostUpdateEventListener>())
                {
                    eventListener.Notify(postArgs);
                }
            }
            catch (Exception ex)
            {
                if (Logger != null) Logger.Error(ex, string.Format("An error occurred while trying to update '{0}'.", distinguishedName));
                throw;
            }

            return distinguishedName;
        }

        internal static string GetDistinguishedName<T>(string distinguishedName, IObjectMapping objectMapping, T entry)
        {
            if (distinguishedName.IsNullOrEmpty())
            {
                var distinguishedNameMapping = objectMapping.GetDistinguishedNameMapping();

                if (distinguishedNameMapping == null) throw new MappingException("Distinguished name must be mapped.");

                distinguishedName = distinguishedNameMapping.GetValue(entry) as string;

                if (distinguishedName.IsNullOrEmpty()) throw new ArgumentException("The distinguished name cannot be null or empty.");
            }

            return distinguishedName;
        }

#if !NET35 && !NET40

        /// <summary>
        /// Adds the entry to the directory.
        /// </summary>
        /// <param name="entry">The attributes for the entry</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="entry"/> is null.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the add was not successful.</exception>
        /// <exception cref="LdapException">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task AddEntryAsync(IDirectoryAttributes entry, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            await _connection.AddAsync(entry, Logger, controls, _configuration.GetListeners<IAddEventListener>(), resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds the entry to the directory and returns the newly saved entry from the directory.
        /// </summary>
        /// <param name="entry">The attributes for the entry</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="entry"/> is null.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the add was not successful.</exception>
        /// <exception cref="LdapException">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task<IDirectoryAttributes> AddAndGetEntryAsync(IDirectoryAttributes entry, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            return await _connection.AddAndGetAsync(entry, Logger, controls, _configuration.GetListeners<IAddEventListener>(), resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes an entry from the directory.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name of the entry</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="distinguishedName"/> is null, empty or white space.</exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapException">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task DeleteAsync(string distinguishedName, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            await _connection.DeleteAsync(distinguishedName, Logger, controls, _configuration.GetListeners<IDeleteEventListener>(), resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the entry in the directory. If the <paramref name="distinguishedName"/> is
        /// null then a mapped distinguished name property is used.
        /// </summary>
        /// <param name="entry">The entry to update</param>
        /// <param name="distinguishedName">The distinguished name for the entry. Ignored if <typeparamref name="T"/> is an instance of <see cref="IDirectoryAttributes"/></param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <typeparam name="T">The type of entry.</typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if entry is null</exception>
        /// <exception cref="MappingException">
        /// Thrown if <paramref name="distinguishedName"/> is null and Distinguished Name is not mapped.
        /// Thrown if object class or object category have not been mapped.
        /// Thrown if <typeparamref name="T"/> has not been mapped.
        /// </exception>
        /// <exception cref="ArgumentException">Thrown if distinguished name is null and there is no mapped distinguished name property.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <paramref name="entry"/> is <see cref="DirectoryObjectBase"/> but the entry is not tracking changes.</exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation is not successful</exception>
        /// <exception cref="LdapException">Thrown if the operation is not successful</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task UpdateAsync<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing) where T : class
        {
            if (entry is IDirectoryAttributes x) await UpdateEntryAsync(x, controls, resultProcessing).ConfigureAwait(false);
            else await UpdateEntryAsync(entry, distinguishedName, controls, resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the entry in the directory and returns the updated version from the directory. If the <paramref name="distinguishedName"/> is
        /// null then a mapped distinguished name property is used.
        /// </summary>
        /// <param name="entry">The entry to update</param>
        /// <param name="distinguishedName">The distinguished name for the entry. Ignored if <typeparamref name="T"/> is an instance of <see cref="IDirectoryAttributes"/></param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <typeparam name="T">The type of entry.</typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if entry is null</exception>
        /// <exception cref="MappingException">
        /// Thrown if <paramref name="distinguishedName"/> is null and Distinguished Name is not mapped.
        /// Thrown if <typeparamref name="T"/> has not been mapped.
        /// </exception>
        /// <exception cref="ArgumentException">Thrown if distinguished name is null and there is no mapped distinguished name property.</exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation is not successful</exception>
        /// <exception cref="LdapException">Thrown if the operation is not successful</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task<T> UpdateAndGetAsync<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing) where T : class
        {
            if (entry is IDirectoryAttributes x)
            {
                return (await UpdateAndGetEntryAsync(x, controls, resultProcessing).ConfigureAwait(false)) as T;
            }
            else
            {
                var dn = await UpdateEntryAsync(entry, distinguishedName, controls, resultProcessing).ConfigureAwait(false);

                return await GetByDNAsync<T>(dn, resultProcessing).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates the entry in the directory and returns the updated version from the directory.
        /// </summary>
        /// <param name="entry">The entry to update.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="entry"/> is null.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails</exception>
        /// <exception cref="LdapException">Thrown if the operation fails</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task<IDirectoryAttributes> UpdateAndGetEntryAsync(IDirectoryAttributes entry, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return await _connection.UpdateAndGetAsync(entry, Logger, controls, _configuration.GetListeners<IUpdateEventListener>(), resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the entry in the directory.
        /// </summary>
        /// <param name="entry">The entry to update.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="entry"/> is null.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails</exception>
        /// <exception cref="LdapException">Thrown if the operation fails</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task UpdateEntryAsync(IDirectoryAttributes entry, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            await _connection.UpdateAsync(entry, Logger, controls, _configuration.GetListeners<IUpdateEventListener>(), resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds the attribute to an entry.
        /// </summary>
        /// <param name="distinguishedName">The entry</param>
        /// <param name="attributeName">The name of the attribute</param>
        /// <param name="value">The value for the entry.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapConnection">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task AddAttributeAsync(string distinguishedName, string attributeName, object value = null, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            if (distinguishedName.IsNullOrEmpty())
                throw new ArgumentNullException("distinguishedName");

            var attributes = new DirectoryAttributes(distinguishedName);

            attributes.AddModification(value.ToDirectoryModification(attributeName, DirectoryAttributeOperation.Add));

            await _connection.UpdateAsync(attributes, Logger, controls, _configuration.GetListeners<IUpdateEventListener>(), resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the attribute from an entry.
        /// </summary>
        /// <param name="distinguishedName">The entry</param>
        /// <param name="attributeName">The name of the attribute</param>
        /// <param name="value">The optional value. If null the whole attribute will be removed, otherwise the value will be removed.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="distinguishedName"/> is null, empty or white space.</exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapConnection">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task DeleteAttributeAsync(string distinguishedName, string attributeName, object value = null, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            if (distinguishedName.IsNullOrEmpty())
                throw new ArgumentNullException("distinguishedName");

            var attributes = new DirectoryAttributes(distinguishedName);

            attributes.AddModification(value.ToDirectoryModification(attributeName, DirectoryAttributeOperation.Delete));

            await _connection.UpdateAsync(attributes, Logger, controls, _configuration.GetListeners<IUpdateEventListener>(), resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves the entry from one container to another without modifying the entry's name.
        /// </summary>
        /// <param name="currentDistinguishedName">The entry's current distinguished name</param>
        /// <param name="newNamingContext">The new container for the entry</param>
        /// <param name="deleteOldRDN">Maps to <see cref="P:System.DirectoryServices.Protocols.ModifyDNRequest.DeleteOldRdn"/>. Defaults to null to use default behavior from <see cref="P:System.DirectoryServices.Protocols.ModifyDNRequest.DeleteOldRdn"/>.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="currentDistinguishedName"/> has an invalid format.
        /// </exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="currentDistinguishedName"/>
        /// or <paramref name="newNamingContext"/> are null, empty or white space.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapConnection">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task<string> MoveEntryAsync(string currentDistinguishedName, string newNamingContext, bool? deleteOldRDN = null,
            DirectoryControl[] controls = null, PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return await _connection.MoveEntryAsync(currentDistinguishedName, newNamingContext, Logger, deleteOldRDN, controls, resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Renames the entry within the same container. The <paramref name="newName"/> can be in the format
        /// XX=New Name or just New Name.
        /// </summary>
        /// <param name="currentDistinguishedName">The entry's current distinguished name</param>
        /// <param name="newName">The new name of the entry</param>
        /// <param name="deleteOldRDN">Maps to <see cref="P:System.DirectoryServices.Protocols.ModifyDNRequest.DeleteOldRdn"/>. Defaults to null to use default behavior from <see cref="P:System.DirectoryServices.Protocols.ModifyDNRequest.DeleteOldRdn"/>.</param>
        /// <param name="controls">Any <see cref="DirectoryControl"/>s to be sent with the request</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="currentDistinguishedName"/> has an invalid format.
        /// </exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="currentDistinguishedName"/>
        /// or <paramref name="newName"/> are null, empty or white space.
        /// </exception>
        /// <exception cref="DirectoryOperationException">Thrown if the operation fails.</exception>
        /// <exception cref="LdapConnection">Thrown if the operation fails.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        public async Task<string> RenameEntryAsync(string currentDistinguishedName, string newName, bool? deleteOldRDN = null, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
            return await _connection.RenameEntryAsync(currentDistinguishedName, newName, Logger, deleteOldRDN, controls, resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Uses range retrieval to get all values for <paramref name="attributeName"/> on <paramref name="distinguishedName"/>.
        /// </summary>
        /// <typeparam name="TValue">The type of the attribute.  Must be <see cref="string"/> or <see cref="Array"/> of <see cref="byte"/>.</typeparam>
        /// <param name="distinguishedName">The distinguished name of the entry.</param>
        /// <param name="attributeName">The attribute to load.</param>
        /// <param name="start">The starting point for the range. Defaults to 0.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="distinguishedName"/> or <paramref name="attributeName"/> is null, empty or white space.
        /// </exception>
        /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
        /// <returns></returns>
        public async Task<IList<TValue>> RetrieveRangesAsync<TValue>(string distinguishedName, string attributeName, int start = 0,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

            return await _connection.RetrieveRangesAsync<TValue>(distinguishedName, attributeName, start, Logger, resultProcessing).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends the request to the directory.
        /// </summary>
        /// <param name="request">The response from the directory</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <returns></returns>
        public async Task<DirectoryResponse> SendRequestAsync(DirectoryRequest request, PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);

#if NET45
            return await Task.Factory.FromAsync(
                    (callback, state) =>
                    {
                        return _connection.BeginSendRequest(request, resultProcessing, callback, state);
                    },
                    (asyncresult) =>
                    {
                        return _connection.EndSendRequest(asyncresult);
                    },
                    null
                ).ConfigureAwait(false);
#else
            return await Task.Run(() => _connection.SendRequest(request) as DirectoryResponse).ConfigureAwait(false);
#endif
        }

        private async Task<string> UpdateEntryAsync<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing)
            where T : class
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                if (entry == null) throw new ArgumentNullException(nameof(entry));

                var objectMapping = _configuration.Mapper.GetMapping(entry.GetType());
                if (objectMapping == null) throw new MappingException("Cannot update an unmapped class.");

                distinguishedName = GetDistinguishedName(distinguishedName, objectMapping, entry);

                var request = new ModifyRequest(distinguishedName);
                if (controls != null)
                {
                    request.Controls.AddRange(controls);
                }

                var modifications = new List<DirectoryAttributeModification>();

                if (!(entry is IDirectoryObject directoryObject))
                {
                    modifications.AddRange(objectMapping.GetPropertyMappingsForUpdate()
                        .Select(mapping => mapping.GetDirectoryAttributeModification(entry)));
                }
                else
                {
                    var changes = directoryObject.GetChanges(objectMapping);
                    modifications.AddRange(changes);
                }

                if (objectMapping.GetCatchAllMapping()?.GetValue(entry) is IDirectoryAttributes catchAll)
                {
                    modifications.AddRange(catchAll.GetChangedAttributes());
                }

                if (modifications.Count == 0)
                {
                    if (Logger != null && Logger.TraceEnabled) Logger.Trace(string.Format("No changes found for {0}.", distinguishedName));

                    return distinguishedName;
                }

                request.Modifications.AddRange(modifications.ToArray());

                var preArgs = new ListenerPreArgs<object, ModifyRequest>(entry, request, _connection);
                foreach (var eventListener in _configuration.GetListeners<IPreUpdateEventListener>())
                {
                    eventListener.Notify(preArgs);
                }

                if (Logger != null && Logger.TraceEnabled) Logger.Trace(request.ToLogString());

                ModifyResponse response = null;
#if NET45
                await Task.Factory.FromAsync(
                        (callback, state) =>
                        {
                            return _connection.BeginSendRequest(request, resultProcessing, callback, state);
                        },
                        (asyncresult) =>
                        {
                            response = _connection.EndSendRequest(asyncresult) as ModifyResponse;
                        },
                        null
                    ).ConfigureAwait(false);
#else
                response = await Task.Run(() => _connection.SendRequest(request) as ModifyResponse).ConfigureAwait(false);
#endif
                response.AssertSuccess();

                var postArgs = new ListenerPostArgs<object, ModifyRequest, ModifyResponse>(entry, request, response, _connection);
                foreach (var eventListener in _configuration.GetListeners<IPostUpdateEventListener>())
                {
                    eventListener.Notify(postArgs);
                }
            }
            catch (Exception ex)
            {
                if (Logger != null) Logger.Error(ex, string.Format("An error occurred while trying to update '{0}'.", distinguishedName));
                throw;
            }

            return distinguishedName;
        }

        private async Task<string> AddEntryAsync<T>(T entry, string distinguishedName = null, DirectoryControl[] controls = null,
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing) where T : class
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(GetType().FullName);
                if (entry == null) throw new ArgumentNullException(nameof(entry));
                var objectMapping = _configuration.Mapper.GetMapping(entry.GetType());
                if (objectMapping == null) throw new MappingException("Cannot add an unmapped class.");

                var attributes = new List<DirectoryAttribute>();

                if (objectMapping.ObjectClasses != null && objectMapping.ObjectClasses.Any())
                {
                    attributes.Add(new DirectoryAttribute("objectClass", objectMapping.ObjectClasses.Select(oc => (object)oc).ToArray()));
                }
                else
                {
                    throw new MappingException(
                        $"Cannot add an entry without mapping objectClass for {typeof(T).FullName}.");
                }

                distinguishedName = GetDistinguishedName(distinguishedName, objectMapping, entry);

                var request = new AddRequest(distinguishedName);

                if (controls != null)
                {
                    request.Controls.AddRange(controls);
                }

                var directoryAttributes = objectMapping.GetPropertyMappingsForAdd()
                    .Select(pm => pm.GetDirectoryAttribute(entry))
                    .Where(da => da.Count > 0);

                var catchAll =
                    objectMapping.GetCatchAllMapping()?.GetValue(entry) as IDirectoryAttributes;

                catchAll?.GetChangedAttributes().Where(da => da.Count > 0).ForEach(x => attributes.Add(x));

                foreach (var da in directoryAttributes.Union(attributes))
                {
                    request.Attributes.Add(da);
                }

                var preArgs = new ListenerPreArgs<object, AddRequest>(entry, request, _connection);
                foreach (var eventListener in _configuration.GetListeners<IPreAddEventListener>())
                {
                    eventListener.Notify(preArgs);
                }

                if (Logger != null && Logger.TraceEnabled) Logger.Trace(request.ToLogString());

                AddResponse response = null;
#if NET45
                await Task.Factory.FromAsync(
                        (callback, state) =>
                        {
                            return _connection.BeginSendRequest(request, resultProcessing, callback, state);
                        },
                        (asyncresult) =>
                        {
                            response = _connection.EndSendRequest(asyncresult) as AddResponse;
                        },
                        null
                    ).ConfigureAwait(false);
#else
                response = await Task.Run(() => _connection.SendRequest(request) as AddResponse).ConfigureAwait(false);
#endif

                response.AssertSuccess();

                var postArgs = new ListenerPostArgs<object, AddRequest, AddResponse>(entry, request, response, _connection);
                foreach (var eventListener in _configuration.GetListeners<IPostAddEventListener>())
                {
                    eventListener.Notify(postArgs);
                }
            }
            catch (Exception ex)
            {
                if (Logger != null) Logger.Error(ex, string.Format("An error occurred while trying to add '{0}'.", distinguishedName));
                throw;
            }

            return distinguishedName;
        }

#endif

        /// <summary>
        /// Finalizer that disposes of this class.
        /// </summary>
        ~DirectoryContext()
        {
            Dispose(false);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            if (_disposed) return;
            Dispose(true);
            GC.SuppressFinalize(this);
            _disposed = true;
        }

        private void Dispose(bool disposing)
        {
            var disposeOfConnection = _disposeOfConnection;
            var connectionIsFromFactory = _connectionIsFromFactory;
            if (disposeOfConnection)
            {
                var connection = _connection;
                if (connection != null)
                {
                    var configuration = _configuration;

                    if (configuration != null && connectionIsFromFactory)
                    {
                        var connectionFactory = configuration.ConnectionFactory;
                        if (connectionFactory != null)
                        {
                            if (connectionFactory is IPooledLdapConnectionFactory || disposing)
                                connectionFactory.ReleaseConnection(connection);
                            connectionFactory = null;
                        }
                        else if (disposing)
                        {
                            connection.Dispose();
                        }
                        configuration = null;
                    }
                    else if (disposing)
                    {
                        connection.Dispose();
                    }
                    connection = null;
                }
            }

            _configuration = null;
            _connection = null;
            Logger = null;
        }
    }
}