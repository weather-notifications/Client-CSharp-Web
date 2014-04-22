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
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Calq.Client.Web
{
    /// <summary>
    /// Wraps all the infor needed to make an API request.
    /// </summary>
    internal class CalqRequest
    {
        /// <summary>
        /// Gets the API end point this request is for.
        /// </summary>
        public string Endpoint { get; protected set; }

        /// <summary>
        /// Gets data payload to send with this request.
        /// </summary>
        public IDictionary<string, object> Payload { get; protected set; }

        /// <summary>
        /// Number of retries this request has had.
        /// </summary>
        public int Retries;

        /// <summary>
        /// The next time this request should be retried.
        /// </summary>
        public DateTime NextRetry;

        /// <summary>
        /// Creates a new API processor to record events to the given write key.
        /// </summary>
        /// <param name="endpoint">The API endpoint to request to (e.g. Track).</param>
        /// <param name="payload">The data to send.</param>
        public CalqRequest(string endpoint, IDictionary<string, object> payload)
        {
            Endpoint = endpoint;
            Payload = payload;
        }

    }
}
