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
        // Note: Initially any API call is sent to the server immediately whilst the calling thread waits. If call fails 
        //  then (dependant on  error settings) it will be scheduled to be replayed in the background. This allows
        //  us to get instant feedback on API errors, but batch network failures again for later. It would be more 
        //  efficient to batch all calls, but then error handling would have to be done via callbacks as they occur 
        //  later than the invocation. For ease of client implementation this was not done.
        //  
        //  The background processing is triggered by a Task. This is not ideal as it uses a thread from the pool.
        //  The idea is that the request should replay very quickly, and then the Task finished (or that it should
        //  never be created in the first place!).

        /// <summary>
        /// The base URL for calls to API server.
        /// </summary>
        public const string ApiServerBaseUrl = "api.calq.io";

        /// <summary>
        /// Exception hanlding options (Whether or not we should throw exceptions).
        /// </summary>
        public static ExceptionOptions ExceptionOptions = Web.ExceptionOptions.ThrowAllExceptions;

        /// <summary>
        /// The write_key that is used to log actions with this client.
        /// </summary>
        public string WriteKey { get; protected set; }

        /// <summary>
        /// Creates a new API processor to record events to the given write key.
        /// </summary>
        /// <param name="writeKey">The write key to write events for.</param>
        public CalqApiProcessor(string writeKey)
        {
            WriteKey = writeKey;
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

            CalqApiProcessor.MakeApiPost(new CalqRequest("Track", apiParams));
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

            CalqApiProcessor.MakeApiPost(new CalqRequest("Profile", apiParams));
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

            CalqApiProcessor.MakeApiPost(new CalqRequest("Transfer", apiParams));
        }

         /// <summary>
        /// Issues the given request to the server.
        /// </summary>
        /// <param name="endpoint">The API endpoint to request to (e.g. Track).</param>
        /// <param name="payload">The data to send.</param>
        internal static void MakeApiPost(CalqRequest request)
        {
            var json = JsonConvert.SerializeObject(request.Payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            var req = HttpWebRequest.Create(new Uri(String.Format("{0}://{1}/{2}", "http", ApiServerBaseUrl, request.Endpoint)));
            req.Method = "POST";
            req.ContentType = "application/json";
            req.ContentLength = bytes.Length;

            try
            {
                using (var writer = req.GetRequestStream())
                {
                    writer.Write(bytes, 0, bytes.Length);
                    writer.Close();
                }

                var response = req.GetResponse();
            }
            catch(WebException ex)
            {                
                // Can we parse this failed response? Might be an API exception
                if (ex.Status == WebExceptionStatus.ProtocolError)
                {
                    string response = null;
                    try
                    {
                        using(var stream = ex.Response.GetResponseStream())
                        {
                            Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
                            StreamReader readStream = new StreamReader(stream, encode);
                            response = readStream.ReadToEnd();
                        }
                    }
                    catch(Exception)
                    {
                        // Failed to read the stream. Will fall through to default handler
                    }
                        
                    if(!string.IsNullOrEmpty(response))
                    {
                        try
                        {
                            JObject jsonResponse = JsonConvert.DeserializeObject<dynamic>(response);
                            var errorNode = jsonResponse.Property("error");
                            if (errorNode != null)
                            {
                                if (CalqApiProcessor.ExceptionOptions != Web.ExceptionOptions.None)
                                {
                                    throw (new ApiException(errorNode.Value.ToString()));
                                }
                            }
                        }
                        catch(Newtonsoft.Json.JsonReaderException)
                        {
                            // Not an API error. Swallow and let fall through to next error handler
                        }
                    }
                }

                // Network error that we can retry? (Unless throw all is set)
                if (CalqApiProcessor.ExceptionOptions != Web.ExceptionOptions.ThrowAllExceptions &&
                    (ex.Status == WebExceptionStatus.ConnectFailure ||
                     ex.Status == WebExceptionStatus.NameResolutionFailure ||
                     ex.Status == WebExceptionStatus.SecureChannelFailure ||
                     ex.Status == WebExceptionStatus.Timeout))
                {
                    // Will just throw away if we hit the retry limit (which is fine)
                    CalqApiBackgroundProcessor.ScheduleRetry(request);
                }
                else
                {
                    // Wasn't an API error, or was unhandled. Just throw it
                    if (CalqApiProcessor.ExceptionOptions == Web.ExceptionOptions.ThrowAllExceptions)
                    {
                        throw;
                    }
                }
            }
            
        }

        /// <summary>
    }
}
