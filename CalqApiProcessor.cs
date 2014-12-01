//  Copyright 2014 Calq.io
//
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in 
//  compliance with the License. You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software distributed under the License is 
//  distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
//  implied. See the License for the specific language governing permissions and limitations under the 
//  License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Calq.Client.Web
{
    /// <summary>
    /// Processes requests to call API end points. 
    /// </summary>
    public class CalqApiProcessor
    {
        /// <summary>
        /// The write_key that is used to log actions with this client (we can't share ApiProcessors between keys as we batch together).
        /// </summary>
        public string WriteKey { get; protected set; }

        /// <summary>
        /// The dispatcher we use to make API calls.
        /// </summary>
        protected CalqApiDispatcher Dispatcher;

        /// <summary>
        /// The API endpoint for Track calls.
        /// </summary>
        internal const String EndpointTrack = "Track";

        /// <summary>
        /// The API endpoint for Profile calls.
        /// </summary>
        internal const String EndpointProfile = "Profile";

        /// <summary>
        /// The API endpoint for Transfer calls (alias)
        /// </summary>
        internal const String EndpointTransfer = "Transfer";

        /// <summary>
        /// Creates a new API processor to record events to the given write key.
        /// </summary>
        /// <param name="writeKey">The write key to write events for.</param>
        public CalqApiProcessor(string writeKey)
        {
            WriteKey = writeKey;
            Dispatcher = new CalqApiDispatcher();
        }

        /// <summary>
        /// Makes a call to the Track endpoint to log an action occurrence.
        /// </summary>
        /// <param name="actor">The actor this call is for.</param>
        /// <param name="action">The name of the action being logged.</param>
        /// <param name="apiParams">Additional api properties for this call (e.g. ip address)</param>
        /// <param name="userProperties">Custom user properties for this action.</param>
        public void Track(string actor, string action, IDictionary<string, object> apiParams, IDictionary<string, object> userProperties)
        {
            if (apiParams == null)
            {
                throw (new ArgumentNullException("apiParams"));
            }
            if(userProperties == null)
            {
                throw (new ArgumentNullException("userProperties"));
            }

            // Stuff everything into the apiProperties then JSON it for the payload
            apiParams[ReservedApiProperties.Actor] = actor;
            apiParams[ReservedApiProperties.ActionName] = action;
            apiParams[ReservedApiProperties.ProjectWriteKey] = WriteKey;
            apiParams[ReservedApiProperties.UserProperties] = userProperties;
            apiParams[ReservedApiProperties.Timestamp] = DateTime.UtcNow;

            Dispatcher.EnqueueApiCall(new CalqRequest(EndpointTrack, apiParams));
        }

        /// <summary>
        /// Makes a call to the Profile endpoint to save information about a user.
        /// </summary>
        /// <param name="actor">The name of the action being logged.</param>
        /// <param name="userProperties">Custom user properties for this action.</param>
        public void Profile(string actor, IDictionary<string, object> userProperties)
        {
            if (userProperties == null || userProperties.Count == 0)
            {
                throw (new ArgumentNullException("userProperties"));
            }

            // Stuff everything into the apiProperties then JSON it for the payload
            var apiParams = new Dictionary<string, object>();
            apiParams[ReservedApiProperties.Actor] = actor;
            apiParams[ReservedApiProperties.ProjectWriteKey] = WriteKey;
            apiParams[ReservedApiProperties.UserProperties] = userProperties;

            Dispatcher.EnqueueApiCall(new CalqRequest(EndpointProfile, apiParams));
        }
        
        /// <summary>
        /// Makes a call to the Transfer endpoint to associate anonymous actions with new actions.
        /// </summary>
        /// <param name="oldActor">The previous name of the actor.</param>
        /// <param name="newActor">The new name of the actor.</param>
        public void Transfer(string oldActor, string newActor)
        {
            if (string.IsNullOrEmpty(oldActor))
            {
                throw (new ArgumentNullException("oldActor"));
            }
            if (string.IsNullOrEmpty(newActor))
            {
                throw (new ArgumentNullException("newActor"));
            }

            var apiParams = new Dictionary<string, object>();
            apiParams[ReservedApiProperties.OldActor] = oldActor;
            apiParams[ReservedApiProperties.NewActor] = newActor;
            apiParams[ReservedApiProperties.ProjectWriteKey] = WriteKey;

            Dispatcher.EnqueueApiCall(new CalqRequest(EndpointTransfer, apiParams));
        }

    }
}
