﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using RestSharp.Portable.Deserializers;
using RestSharp.Portable.WebRequest.Impl;

namespace RestSharp.Portable.WebRequest
{
    /// <summary>
    /// The default REST client
    /// </summary>
    public class RestClient : IRestClient
    {
        private readonly IDictionary<string, IDeserializer> _contentHandlers = new Dictionary<string, IDeserializer>(StringComparer.OrdinalIgnoreCase);

        private readonly IList<string> _acceptTypes = new List<string>();

        private readonly IDictionary<string, IEncoding> _encodingHandlers = new Dictionary<string, IEncoding>(StringComparer.OrdinalIgnoreCase);

        private readonly IList<string> _acceptEncodings = new List<string>();

        private readonly List<Parameter> _defaultParameters = new List<Parameter>();

        private IHttpClient _httpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="RestClient" /> class.
        /// </summary>
        public RestClient()
        {
            var jsonDeserializer = new JsonDeserializer();

            // register default handlers
            AddHandler("application/json", jsonDeserializer);
            AddHandler("text/json", jsonDeserializer);
            AddHandler("text/x-json", jsonDeserializer);
            AddHandler("text/javascript", jsonDeserializer);

            var xmlDataContractDeserializer = new XmlDataContractDeserializer();
            AddHandler("application/xml", xmlDataContractDeserializer);
            AddHandler("text/xml", xmlDataContractDeserializer);

            HttpClientFactory = new WebRequestHttpClientFactory();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RestClient" /> class.
        /// </summary>
        /// <param name="baseUrl">Base URL</param>
        public RestClient(Uri baseUrl)
            : this()
        {
            BaseUrl = baseUrl;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RestClient" /> class.
        /// </summary>
        /// <param name="baseUrl">Base URL</param>
        public RestClient(string baseUrl)
            : this(new Uri(baseUrl))
        {
        }

        /// <summary>
        /// Gets or sets the HTTP client factory used to create IHttpClient implementations
        /// </summary>
        public IHttpClientFactory HttpClientFactory { get; set; }

        /// <summary>
        /// Gets or sets the authenticator to use for all requests
        /// </summary>
        public IAuthenticator Authenticator { get; set; }

        /// <summary>
        /// Gets or sets the base URL for all requests
        /// </summary>
        public Uri BaseUrl { get; set; }

        /// <summary>
        /// Gets the default parameters for all requests
        /// </summary>
        public IList<Parameter> DefaultParameters
        {
            get { return _defaultParameters; }
        }

        /// <summary>
        /// Gets or sets the cookies for all requests
        /// </summary>
        /// <remarks>
        /// Cookies set by the server will be collected here.
        /// </remarks>
        public CookieContainer CookieContainer { get; set; }

        /// <summary>
        /// Gets or sets a proxy to use for the requests
        /// </summary>
        public IRequestProxy Proxy { get; set; }

        /// <summary>
        /// Gets or sets the credentials used for the request (e.g. NTLM authentication)
        /// </summary>
        public ICredentials Credentials { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the response status code should be ignored?
        /// </summary>
        public bool IgnoreResponseStatusCode { get; set; }

        /// <summary>
        /// Gets or sets the default <see cref="StringComparer"/> to be used for the requests.
        /// </summary>
        /// <remarks>
        /// If this property is null, the <see cref="StringComparer.Ordinal"/> is used.
        /// </remarks>
        public StringComparer DefaultParameterNameComparer { get; set; }

        /// <summary>
        /// Gets or sets the timeout to be used for requests.
        /// </summary>
        /// <remarks>
        /// When the value isn't set, it uses the default timeout of the underlying HTTP client (100 seconds) or whatever
        /// is used to execute the HTTP requests.
        /// </remarks>
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// Execute the given request
        /// </summary>
        /// <param name="request">Request to execute</param>
        /// <returns>Response returned</returns>
        public async Task<IRestResponse> Execute(IRestRequest request)
        {
            using (var response = await ExecuteRequest(request, CancellationToken.None))
            {
                return await RestResponse.CreateResponse(this, request, response);
            }
        }

        /// <summary>
        /// Execute the given request
        /// </summary>
        /// <typeparam name="T">The type to deserialize to</typeparam>
        /// <param name="request">Request to execute</param>
        /// <returns>Response returned, with a deserialized object</returns>
        public async Task<IRestResponse<T>> Execute<T>(IRestRequest request)
        {
            using (var response = await ExecuteRequest(request, CancellationToken.None))
            {
                return await RestResponse.CreateResponse<T>(this, request, response);
            }
        }

        /// <summary>
        /// Cancellable request execution
        /// </summary>
        /// <param name="request">Request to execute</param>
        /// <param name="ct">The cancellation token</param>
        /// <returns>Response returned</returns>
        public async Task<IRestResponse> Execute(IRestRequest request, CancellationToken ct)
        {
            using (var response = await ExecuteRequest(request, ct))
            {
                return await RestResponse.CreateResponse(this, request, response);
            }
        }

        /// <summary>
        /// Cancellable request execution
        /// </summary>
        /// <typeparam name="T">The type to deserialize to</typeparam>
        /// <param name="request">Request to execute</param>
        /// <param name="ct">The cancellation token</param>
        /// <returns>Response returned, with a deserialized object</returns>
        public async Task<IRestResponse<T>> Execute<T>(IRestRequest request, CancellationToken ct)
        {
            using (var response = await ExecuteRequest(request, ct))
            {
                return await RestResponse.CreateResponse<T>(this, request, response);
            }
        }

        /// <summary>
        /// Add a new content type handler
        /// </summary>
        /// <param name="contentType">The Accept header value</param>
        /// <param name="deserializer">The deserializer to decode the content</param>
        /// <returns>The client itself, to allow call chains</returns>
        public IRestClient AddHandler(string contentType, IDeserializer deserializer)
        {
            _contentHandlers[contentType] = deserializer;
            if (contentType != "*")
            {
                _acceptTypes.Add(contentType);
                UpdateAcceptsHeader();
            }

            return this;
        }

        /// <summary>
        /// Remove a previously added content type handler
        /// </summary>
        /// <param name="contentType">The Accept header value that identifies the handler</param>
        /// <returns>The client itself, to allow call chains</returns>
        public IRestClient RemoveHandler(string contentType)
        {
            _contentHandlers.Remove(contentType);
            if (contentType != "*")
            {
                _acceptTypes.Remove(contentType);
                UpdateAcceptsHeader();
            }

            return this;
        }

        /// <summary>
        /// Remove all previously added content type handlers
        /// </summary>
        /// <returns>The client itself, to allow call chains</returns>
        public IRestClient ClearHandlers()
        {
            _contentHandlers.Clear();
            _acceptTypes.Clear();
            UpdateAcceptsHeader();
            return this;
        }

        /// <summary>
        /// Get a previously added content type handler
        /// </summary>
        /// <param name="contentType">The Accept header value that identifies the handler</param>
        /// <returns>The deserializer that can handle the given content type.</returns>
        /// <remarks>
        /// This function returns NULL if the handler for the given content type cannot be found.
        /// </remarks>
        public IDeserializer GetHandler(string contentType)
        {
            if (string.IsNullOrEmpty(contentType) && _contentHandlers.ContainsKey("*"))
                return _contentHandlers["*"];
            if (contentType == null)
                contentType = string.Empty;
            var semicolonIndex = contentType.IndexOf(';');
            if (semicolonIndex != -1)
                contentType = contentType.Substring(0, semicolonIndex).TrimEnd();
            if (_contentHandlers.ContainsKey(contentType))
                return _contentHandlers[contentType];
            if (_contentHandlers.ContainsKey("*"))
                return _contentHandlers["*"];
            return null;
        }

        /// <summary>
        /// Replace all handlers of a given type with a new deserializer
        /// </summary>
        /// <param name="oldType">The type of the old deserializer</param>
        /// <param name="deserializer">The new deserializer</param>
        /// <returns>The client itself, to allow call chains</returns>
        public IRestClient ReplaceHandler(Type oldType, IDeserializer deserializer)
        {
            var contentHandlersToReplace = _contentHandlers.Where(x => x.Value.GetType().GetTypeInfo().IsAssignableFrom(oldType.GetTypeInfo())).ToList();
            foreach (var contentHandlerToReplace in contentHandlersToReplace)
            {
                _contentHandlers.Remove(contentHandlerToReplace.Key);
                _contentHandlers.Add(contentHandlerToReplace.Key, deserializer);
            }

            UpdateAcceptsHeader();
            return this;
        }

        /// <summary>
        /// Add a new content encoding handler
        /// </summary>
        /// <param name="encodingId">The Accept-Encoding header value</param>
        /// <param name="encoding">The encoding engine to decode the content</param>
        /// <returns>The client itself, to allow call chains</returns>
        public IRestClient AddEncoding(string encodingId, IEncoding encoding)
        {
            _encodingHandlers[encodingId] = encoding;
            _acceptEncodings.Add(encodingId);
            UpdateAcceptsEncodingHeader();
            return this;
        }

        /// <summary>
        /// Remove a previously added content encoding handler
        /// </summary>
        /// <param name="encodingId">The Accept-Encoding header value that identifies the handler</param>
        /// <returns>The client itself, to allow call chains</returns>
        public IRestClient RemoveEncoding(string encodingId)
        {
            _encodingHandlers.Remove(encodingId);
            _acceptEncodings.Remove(encodingId);
            UpdateAcceptsEncodingHeader();
            return this;
        }

        /// <summary>
        /// Remove all previously added content encoding handlers
        /// </summary>
        /// <returns>The client itself, to allow call chains</returns>
        public IRestClient ClearEncodings()
        {
            _encodingHandlers.Clear();
            _acceptEncodings.Clear();
            UpdateAcceptsEncodingHeader();
            return this;
        }

        /// <summary>
        /// Get a previously added content encoding handler
        /// </summary>
        /// <param name="encodingIds">The Accept-Encoding header value that identifies the handler</param>
        /// <returns>The handler that can decode the given content encoding.</returns>
        /// <remarks>
        /// This function returns NULL if the handler for the given content encoding cannot be found.
        /// </remarks>
        public IEncoding GetEncoding(IEnumerable<string> encodingIds)
        {
            if (encodingIds != null)
            {
                foreach (var encodingId in encodingIds)
                {
                    if (_encodingHandlers.ContainsKey(encodingId))
                        return _encodingHandlers[encodingId];
                }
            }

            if (_encodingHandlers.ContainsKey("*"))
                return _encodingHandlers["*"];
            return null;
        }

        void IDisposable.Dispose()
        {
        }

        private void UpdateAcceptsHeader()
        {
            this.RemoveDefaultParameter("Accept");
            if (_acceptTypes.Count != 0)
            {
                var accepts = string.Join(", ", _acceptTypes);
                this.AddDefaultParameter(new Parameter
                {
                    Name = "Accept",
                    Value = accepts,
                    Type = ParameterType.HttpHeader,
                    ValidateOnAdd = true,
                });
            }
        }

        private void UpdateAcceptsEncodingHeader()
        {
            this.RemoveDefaultParameter("Accept-Encoding");
            if (_acceptEncodings.Count != 0)
            {
                var accepts = string.Join(", ", _acceptEncodings);
                this.AddDefaultParameter("Accept-Encoding", accepts, ParameterType.HttpHeader);
            }
        }

        /// <summary>
        /// Add overridable default parameters to the request
        /// </summary>
        /// <param name="request">The requests to add the default parameters to.</param>
        private void AddDefaultParameters(IRestRequest request)
        {
            var comparer = new ParameterComparer(this, request);

            var startIndex = 0;
            foreach (var parameter in DefaultParameters.Where(x => x.Type != ParameterType.HttpHeader))
            {
                if (request.Parameters.Contains(parameter, comparer))
                    continue;
                request.Parameters.Insert(startIndex++, parameter);
            }
        }

        private async Task<IHttpResponseMessage> ExecuteRequest(IRestRequest request, CancellationToken ct)
        {
            AddDefaultParameters(request);
            while (true)
            {
                if (Authenticator != null && Authenticator.CanPreAuthenticate(this, request, Credentials))
                    await Authenticator.PreAuthenticate(this, request, Credentials);

                // Lazy initialization of the HTTP client
                if (_httpClient == null)
                    _httpClient = HttpClientFactory.CreateClient(this, request);

                using (var message = HttpClientFactory.CreateRequestMessage(this, request))
                {
                    var bodyData = this.GetContent(request);
                    if (bodyData != null)
                        message.Content = bodyData;

                    if (Authenticator != null && Authenticator.CanPreAuthenticate(_httpClient, message, Credentials))
                        await Authenticator.PreAuthenticate(_httpClient, message, Credentials);

                    IHttpResponseMessage response;
                    bool failed = true;
                    try
                    {
                        response = await _httpClient.SendAsync(message, ct);
                        failed = false;
                    }
                    finally
                    {
                        if (failed)
                            message.Dispose();
                    }

                    failed = true;
                    try
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            if (Authenticator != null && Authenticator.CanHandleChallenge(_httpClient, message, Credentials, response))
                            {
                                await Authenticator.HandleChallenge(_httpClient, message, Credentials, response);
                                continue;
                            }

                            if (!IgnoreResponseStatusCode)
                                response.EnsureSuccessStatusCode();
                        }

                        failed = false;
                    }
                    finally
                    {
                        if (failed)
                        {
                            if (response != null)
                                response.Dispose();
                            else
                                message.Dispose();
                        }
                    }

                    return response;
                }
            }
        }
    }
}