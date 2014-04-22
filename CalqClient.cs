//  Copyright 2014 Calq.io
//
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in 
//  compliance with the License. You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software distributed under the License is 
//  distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
//  implied. See the License for the specific language governing permissions andlimitations under the 
//  License.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Text;
using System.Web;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Calq.Client.Web
{
    /// <summary>
    /// Calq client used to log actions for a specific actor.
    /// </summary>
    public class CalqClient
    {
        #region Private configuration

        /// <summary>
        /// Key used in app/web.config for the write key.
        /// </summary>
        private const string ConfigurationKeyForWriteKey = "CalqWriteKey";

        /// <summary>
        /// The name of the cookie used to write client info. Needs to match JS client.
        /// </summary>
        private const string CalqClientCookieName = "_calq_d";

        /// <summary>
        /// The content of the IP address field if no IP is found.
        /// </summary>
        protected const string CalqClientNoIp = "none";

        /// <summary>
        /// The duration in days for the length of any client cookie we set (should match JS client).
        /// </summary>
        private const int CalqClientCookieExpiry = 60;

        #endregion

        /// <summary>
        /// The name of the actor (unique id, user id etc) for this client.
        /// </summary>
        public string Actor { get; protected set; }

        /// <summary>
        /// Whether or not this client is identified or anonymous.
        /// </summary>
        public bool IsAnon { get; protected set; }

        /// <summary>
        /// The processor used to dispatch API calls for this client.
        /// </summary>
        protected CalqApiProcessor ApiProcessor;

        /// <summary>
        /// Whether or not this client has tracked any actions.
        /// </summary>
        protected bool HasTracked;

        /// <summary>
        /// Any global properties associated with this client.
        /// </summary>
        protected IDictionary<string, object> GlobalProperties;

        #region Creating Clients & Client State

        /// <summary>
        /// Creates a new client for the given actor and write key.
        /// </summary>
        /// <param name="actor">The name of the actor (unique id, user id etc) for this client.</param>
        /// <param name="writeKey">The write_key that is used to log actions with this client.</param>
        public CalqClient(string actor, string writeKey)
        {
            if(string.IsNullOrEmpty(actor))
            {
                throw (new ArgumentNullException("actor", "An actor parameter must be specified"));
            }
            if(string.IsNullOrEmpty(writeKey) || writeKey.Length < 32 /* Might have longer keys later */)
            {
                // Params other way around on base ArgumentException. Strange design choice MS
                throw (new ArgumentException("A valid write key must be specified", "writeKey")); 
            }

            HasTracked = false;
            IsAnon = false;
            Actor = actor;
            GlobalProperties = new Dictionary<string, object>();

            ApiProcessor = new CalqApiProcessor(writeKey);
        }

        /// <summary>
        /// Creates a new client for the given actor and attempts to read the write key from app configuration.
        /// </summary>
        /// <param name="actor">The name of the actor (unique id, user id etc) for this client.</param>
        public CalqClient(string actor)
            : this(actor, CalqClient.GetWriteKeyFromConfig()) {}

        /// <summary>
        /// Creates a CalqClient using the current web request. This attempts to get the calq user information
        /// from the cookie in the current request. If one is not present then a new one is created.
        /// </summary>
        /// <param name="writeKey">The write_key that is used to log actions with this client.</param>
        public static CalqClient FromCurrentRequest(string writeKey)
        {
            if(HttpContext.Current == null)
            {
                throw (new ApplicationException("FromCurrentRequest() called outside of an HTTP request context."));
            }
            var request = HttpContext.Current.Request;

            CalqClient client = new CalqClient(Guid.NewGuid().ToString(), writeKey);    // Guid will be overwritten by parse state if valid
            client.IsAnon = true;
            var cookie = request.Cookies.Get(CalqClient.CalqClientCookieName);
            if(cookie != null)
            {
                client.ParseCookieState(cookie);
            }
            else
            {
                // New client, pass back to JS client
                client.WriteCookieState(HttpContext.Current.Response);
            }
            // Read this each time
            if (!String.IsNullOrEmpty(request.UserAgent))
            {
                client.SetGlobalProperty(ReservedActionProperties.DeviceAgent, request.UserAgent);
            }

            return client;
        }

        /// <summary>
        /// Creates a CalqClient using the current web request. This attempts to get the calq user information
        /// from the cookie in the current request. If one is not present then a new one is created.
        /// </summary>
        /// <returns></returns>
        public static CalqClient FromCurrentRequest()
        {
            return CalqClient.FromCurrentRequest(CalqClient.GetWriteKeyFromConfig());
        }

        /// <summary>
        /// Attempts to read the API key from the web/app.config. Will throw exception if not found.
        /// </summary>
        protected static string GetWriteKeyFromConfig()
        {
            var writeKey = ConfigurationManager.AppSettings[CalqClient.ConfigurationKeyForWriteKey];
            if (string.IsNullOrEmpty(writeKey))
            {
                throw (new ConfigurationErrorsException(
                    String.Format("A write_key must be specified in the web/app.config file using the key '{0}'",
                    CalqClient.ConfigurationKeyForWriteKey)));
            }
            return writeKey;
        }

        #endregion

        #region Sync with client state

        // Note: Careful about changes here. The client state here needs to be compatible with client side
        //  clients (normally JavaScript). This client needs to be able to read existing data, and write
        //  data to be read browser side.

        /// <summary>
        /// Writes the state of the client as a cookie to the given request.
        /// </summary>
        /// <param name="response"></param>
        protected void WriteCookieState(HttpResponse response)
        {
            // Note: See ParseCookieState for cookie format information.

            var json = new
            {
                actor = Actor,
                hasAction = HasTracked,
                isAnon = IsAnon,
                actionGlobal = GlobalProperties
            };
            var jsonString = JsonConvert.SerializeObject(json);

            var bytes = Encoding.UTF8.GetBytes(jsonString);
            var encoded = Convert.ToBase64String(bytes);

            var cookie = response.Cookies.Get(CalqClient.CalqClientCookieName); // Get creates new if missing
            cookie.Value = encoded;
            cookie.Expires = DateTime.UtcNow.AddDays(CalqClient.CalqClientCookieExpiry);
            cookie.Path = "/";
            response.Cookies.Set(cookie);
        }

        /// <summary>
        /// Parses the state of the client from cookie data.
        /// </summary>
        /// <param name="cookie">The cookie to parse client state from.</param>
        protected void ParseCookieState(HttpCookie cookie)
        {
            // Current cookie format is Base64 encoded JSON in format:
            //
            //  {
            //        actor: "some_id",
            //        hasAction: true,
            //        isAnon: true,
            //        actionGlobal: {
            //              someGlobalProperty: "someValue"
            //        }
            //  }
            //

            string decoded = null;
            try
            {
                byte[] bytes = Convert.FromBase64String(cookie.Value);
                decoded = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            }
            catch (System.FormatException)
            {
                // Not much we can do. We just don't parse the state and keep the default new Guid
                return;
            }

            JObject json = null;
            try
            {
                json = JsonConvert.DeserializeObject<dynamic>(decoded);

                var actor = json.Property("actor");
                if (actor != null && actor.Value != null)
                {
                    var actorString = actor.Value.ToString();
                    if (!string.IsNullOrEmpty(actorString))
                    {
                        // We MUST have recognised the actor node if we going to parse rest (else we would assign custom data to random ID)
                        Actor = actorString;

                        var globalNode = json.Property("actionGlobal");
                        if (globalNode != null && globalNode.Value != null)
                        {
                            var globalProperties = new Dictionary<string, JToken>();
                            FlattenJsonAttributes((JObject)globalNode.Value, (JObject)globalNode.Value, globalProperties);
                            foreach(var key in globalProperties.Keys)
                            {
                                if(!GlobalProperties.ContainsKey(key))
                                {
                                    GlobalProperties.Add(key, globalProperties[key].ToString());
                                }
                            }
                        }

                        var hasActionNode = json.Property("hasAction");
                        if (hasActionNode != null)
                        {
                            HasTracked = (bool)hasActionNode.Value;
                        }

                        var isAnonNode = json.Property("isAnon");
                        if (isAnonNode != null)
                        {
                            IsAnon = (bool)isAnonNode.Value;
                        }
                    }
                }

            }
            catch (Newtonsoft.Json.JsonReaderException)
            {
                // Not much we can do. We just don't parse the state and keep the default new Guid
            }
        }

        /// <summary>
        /// Flattens the given JObject to the given dictionary. Properties will be seperated by a ".". For example
        /// the object { "foo": { "bar" : "value:" } } will have the flattened property "foo.bar" = "value".
        /// </summary>
        /// <param name="json">The JObject representing this object's data.</param>
        /// <param name="root">The JObject to use as the root of this flatten. Useful if the json is a child of an existing object.</param>
        /// <param name="targetDictionary">The dictionary to add property values to.</param>
        /// <param name="recurse">Whether or not to recurse into children.</param>
        private void FlattenJsonAttributes(JObject json, JObject root, Dictionary<string, JToken> targetDictionary, bool recurse = true)
        {
            string rootPath = root.Path;
            if (!json.Path.StartsWith(rootPath))
            {
                throw (new ArgumentOutOfRangeException("The given root node is not a parent of the given source JSON object."));
            }

            foreach (KeyValuePair<string, JToken> property in json)
            {
                // Step into if this is an object with sub properties, or just record it
                if (property.Value.Type == JTokenType.Object)
                {
                    if (recurse)
                    {
                        FlattenJsonAttributes((JObject)property.Value, root, targetDictionary);
                    }
                }
                else
                {
                    // Don't add null values (strings as the literal "null" are OK)
                    if (property.Value.Type != JTokenType.Null)
                    {
                        // Need to remove "." if root was self
                        var path = property.Value.Path.Substring(rootPath.Length);
                        if (path.StartsWith("."))
                            path = path.Substring(1);
                        targetDictionary.Add(path, property.Value);
                    }
                }
            }
        }

        #endregion

        #region API Methods

        /// <summary>
        /// Tracks an action for the actor this client belongs to. Call this every time your actor does something
        /// that you care about and might want to analyze later.
        /// </summary>
        /// <param name="action">The action to track.</param>
        /// <param name="properties">Additional custom properties to send along with this action (can be null).</param>
        public void Track(string action, IDictionary<string, object> properties = null)
        {
            // Merge super properties into action (User properties have priority if both specified)
            if(properties == null)
            {
                properties = new Dictionary<string, object>();
            }
            foreach (var key in GlobalProperties.Keys)
            {
                if(!properties.ContainsKey(key))
                {
                    properties.Add(key, GlobalProperties[key]);
                }
            }

            // Fill IP address from request if we have one. Important as we are not the client source address
            var ipAddress = HttpContext.Current != null ? GetSourceIpAddress(HttpContext.Current.Request) : CalqClient.CalqClientNoIp;

            var apiProperties = new Dictionary<string, object>();
            apiProperties[ReservedApiProperties.IpAddress] = ipAddress;

            ApiProcessor.Track(Actor, action, apiProperties, properties);

            if (!HasTracked)
            {
                HasTracked = true;
                if (HttpContext.Current != null)
                {
                    WriteCookieState(HttpContext.Current.Response);
                }
            }
        }

        /// <summary>
        /// Tracks an sale action for the actor this client belongs to. Sale actions are actions which have an associated
        /// monetary value (in the form of amount and currency).
        /// </summary>
        /// <param name="action">The action to track.</param>
        /// <param name="properties">Additional custom properties to send along with this action (can be null).</param>
        /// <param name="currency">The 3 letter currency code for this currency (must be 3 letters, doesn't have to be a real currency).</param>
        /// <param name="amount">The amount this sale is for. Specify negative numbers for refunds.</param>
        public void TrackSale(string action, IDictionary<string, object> properties, string currency, decimal amount)
        {
            if(string.IsNullOrEmpty(currency) || currency.Length != 3)
            {
                throw (new ArgumentException("The parameter currency must be a 3 letter currency code (fictional or otherwise).", "currency"));
            }
            
            if (properties == null)
            {
                properties = new Dictionary<string, object>();
            }

            properties[ReservedActionProperties.SaleCurrency] = currency;
            properties[ReservedActionProperties.SaleValue] = amount.ToString();

            Track(action, properties);
        }

        /// <summary>
        /// Sets a global property to be sent with all actions (calls to Track). In a web environment this will also
        /// be written back to the client in the client cookie.
        /// </summary>
        /// <param name="property"></param>
        /// <param name="value"></param>
        public void SetGlobalProperty(string property, object value)
        {
            // Avoid rewriting cookie state unless actually changed
            object existing;
            bool fetched = GlobalProperties.TryGetValue(property, out existing);
            if(!fetched || existing.Equals(value))
            {
                GlobalProperties[property] = value;
                if(HttpContext.Current != null)
                {
                    WriteCookieState(HttpContext.Current.Response);
                }
            }
        }

        /// <summary>
        /// Sets the ID of this client to something else. This should be called if you register/sign-in a user and want
        /// to associate previously anonymous actions with this new identity.
        /// </summary>
        /// <param name="actor">The new actor name.</param>
        public void Identify(string actor)
        {
            if (actor != Actor)
            {
                var oldActor = Actor;
                Actor = actor;
                // Need to issue transfer only if we have sent actions with this client
                if (HasTracked)
                {
                    ApiProcessor.Transfer(oldActor, Actor);
                }

                // Tell client of new state
                IsAnon = false;
                HasTracked = false;
                if (HttpContext.Current != null)
                {
                    WriteCookieState(HttpContext.Current.Response);
                }
            }
        }

        /// <summary>
        /// Sets profile properties for the current user. These are not the same as global properties.
        /// A user must be identified before calling profile.
        /// </summary>
        /// <param name="properties"></param>
        public void Profile(IDictionary<string, object> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                throw (new ArgumentException("You must pass some information to Profile(...) (or else there isn't much point)", "properties"));
            }
            if(IsAnon)
            {
                throw (new ApplicationException("A client must be identified (call Identify(...)) before calling Profile(...)"));
            }

            ApiProcessor.Profile(Actor, properties);
        }

        /// <summary>
        /// Clearrs the current session and resets to being an anonymous user.
        /// </summary>
        public void Clear()
        {
            HasTracked = false;
            Actor = Guid.NewGuid().ToString();
            GlobalProperties = new Dictionary<string, object>();

            if (HttpContext.Current != null)
            {
                WriteCookieState(HttpContext.Current.Response);
            }
        }

        #endregion

        #region Util methods

        /// <summary>
        /// Reads the source IP address from the request.
        /// </summary>
        protected string GetSourceIpAddress(HttpRequest request)
        {
            // Get the submitted IP address from request (Use header if given, behind LB/proxy etc)
            var ipAddress = request.UserHostAddress;
            var xRealIp = request.Headers["X-Real-IP"];
            if (!string.IsNullOrEmpty(xRealIp))
            {
                ipAddress = xRealIp;
            }
            var xForwardedFor = request.Headers["X-Forwarded-For"]; // Favor Forwarded-For over Real-IP header
            if (!string.IsNullOrEmpty(xForwardedFor))
            {
                var split = xForwardedFor.Split(',');
                ipAddress = split[split.Length - 1];
            }

            // Test valid ip4/6 address (Don't trust the header)
            IPAddress test;
            if (!IPAddress.TryParse(ipAddress, out test))
            {
                return CalqClient.CalqClientNoIp;
            }

            return ipAddress;
        }

        #endregion

    }
}