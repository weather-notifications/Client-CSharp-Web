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
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Calq.Client.Web
{
    /// <summary>
    /// Processes requests to call API end points. 
    /// </summary>
    public class CalqApiDispatcher : IRegisteredObject
    {
        /// <summary>
        /// The base URL for calls to API server.
        /// </summary>
        public const string ApiServerBaseUrl = "api.calq.io";
        
        /// <summary>
        /// Max number of actions we batch in a single call (Needs to match Calq API server limits).
        /// </summary>
        public const int MaxBatchedActions = 1000;
        
        /// <summary>
        /// The max number of retries for failed API calls.
        /// </summary>
        public const int MaxRetries = 16;

        /// <summary>
        /// The delay between retrying events (This increases per retry on each event)
        /// </summary>
        public const double InitialRetryDelaySeconds = 60;

        /// <summary>
        /// Queue of outgoing requests to API.
        /// </summary>
        private ConcurrentQueue<CalqRequest> CallQueue;
        
        /// <summary>
        /// Queue of outgoing requests to API end points that have previously failed.
        /// </summary>
        internal LinkedList<CalqRequest> RetryQueue;

        /// <summary>
        /// Lock to create workers.
        /// </summary>
        protected object CreationLock = new object();

        /// <summary>
        /// The retry worker used to dispatch tasks as they come in.
        /// </summary>
        protected Task CurrentDispatchTask;

        /// <summary>
        /// The retry worker used to replay failed requests (longer running).
        /// </summary>
        protected Task CurrentRetryTask;

        /// <summary>
        /// Flag asking our worker to abort when it can.
        /// </summary>
        protected bool StopFlag = false;

        /// <summary>
        /// Creates a new background API processor to dispatch API calls on a background thread.
        /// </summary>
        public CalqApiDispatcher()
        {
            // If we are in a web context, we need to hold this our task open when a pool is recycled (until we have finished flushing)
            if (HostingEnvironment.IsHosted)
            {
                HostingEnvironment.RegisterObject(this);
            }

            CallQueue = new ConcurrentQueue<CalqRequest>();
            RetryQueue = new LinkedList<CalqRequest>();
        }

        /// <summary>
        /// Called when the hosting environment needs us to stop.
        /// </summary>
        /// <param name="immediate">Whether we have a few seconds time to clean up, or if its STOP NOW ZOMG time.</param>
        void IRegisteredObject.Stop(bool immediate)
        {
            StopFlag = true;

            // Wait something sensible - but we can't keep trying forever. Just have to drop any failed requests if this happens
            if (CurrentDispatchTask != null && CurrentDispatchTask.Status == TaskStatus.Running)
            {
                 CurrentDispatchTask.Wait(10 * 1000);
            }
            if (CurrentRetryTask != null && CurrentRetryTask.Status == TaskStatus.Running)
            {
                CurrentRetryTask.Wait(15 * 1000);
            }
        }

        /// <summary>
        /// Enqueues the given API call to be dispatched. Will start background process to dispatch if not currently running.
        /// </summary>
        /// <param name="request"></param>
        internal void EnqueueApiCall(CalqRequest request)
        {
            CallQueue.Enqueue(request);

            lock (CreationLock)
            {
                if (CurrentDispatchTask == null || CurrentDispatchTask.IsCompleted)
                {
                    CurrentDispatchTask = Task.Factory.StartNew(() => { BackgroundDispatchLoop(); });
                }
            }
        }

        /// <summary>
        /// Dispatches any outstanding API calls in the background.
        /// </summary>
        private void BackgroundDispatchLoop()
        {
            while(CallQueue.Count > 0)
            {
                // Keep grouping calls if they are events (which is the only thing that supports batching at the moment)
                List<CalqRequest> batch = new List<CalqRequest>();
                CalqRequest peek;
                while(
                    // Either empty
                    batch.Count == 0 || 
                    // Or we have another Track we can queue (and we are all queues so far)
                    (batch[0].Endpoint == CalqApiProcessor.EndpointTrack && batch.Count < CalqApiDispatcher.MaxBatchedActions
                        && CallQueue.TryPeek(out peek) && peek.Endpoint == CalqApiProcessor.EndpointTrack))
                {
                    if(CallQueue.TryDequeue(out peek))
                    {
                        batch.Add(peek);
                    }
                }

                MakeApiRequest(batch);
            }
        }

        /// <summary>
        /// Schedules retrying on the given request.
        /// </summary>
        /// <param name="request">The request to retry later.</param>
        private void ScheduleRetry(CalqRequest request)
        {
            if (request.Retries < CalqApiDispatcher.MaxRetries)
            {
                lock (RetryQueue)
                {
                    request.NextRetry = DateTime.Now.AddSeconds(InitialRetryDelaySeconds * Math.Pow(1.5, request.Retries + 1));
                    RetryQueue.AddLast(request);
                }
                
                lock (CreationLock)
                {
                    if (CurrentRetryTask == null || CurrentRetryTask.IsCompleted)
                    {
                        CurrentRetryTask = Task.Factory.StartNew(() => { BackgroundRetryLoop(); });
                    }
                }
            }
        }

        /// <summary>
        /// Handles the retrying of requests which have previously failed.
        /// </summary>
        private void BackgroundRetryLoop()
        {
            while (!StopFlag)
            {
                var queue = new Queue<CalqRequest>();
                lock (RetryQueue)
                {
                    // TODO: At some point swap this implementation for a priority queue. Going to be more efficient
                    var node = RetryQueue.First;
                    var now = DateTime.Now;
                    while (node != null)
                    {
                        var next = node.Next;
                        if (node.Value.NextRetry < now || StopFlag) // If stopping, just add everything for one last try
                        {
                            node.List.Remove(node); // Node ref, so doesn't walk
                            queue.Enqueue(node.Value);
                        }
                        node = next;
                    }
                }

                // We play these one at a time. Calq rejects a whole batch as once, but maybe we have only one bad one
                while (queue.Count > 0)
                {
                    var request = queue.Dequeue();
                    request.Retries++;
                    MakeApiRequest(new[] { request });
                }

                // Emptied the queue?
                lock (RetryQueue)
                {
                    if (RetryQueue.Count == 0 || StopFlag)
                    {
                        return;
                    }
                }

                // We flushed everything we could. Check again in a little for more retries
                Thread.Sleep(10 * 1000);
            }
        }

        /// <summary>
        /// Issues the given request batch to Calq's API servers.
        /// </summary>
        /// <param name="batch">The batch of params to issue</param>
        private void MakeApiRequest(IEnumerable<CalqRequest> batch)
        {
            // We only wrap as array if multiple (API won't accept array for non batched methods - e.g. Profile)
            string json = null;
            var first = batch.First();
            if(batch.Count() == 1)
            {
                json = JsonConvert.SerializeObject(first.Payload);
            }
            else
            {
                json = JsonConvert.SerializeObject(batch.Select(a => a.Payload));
            }
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            var req = HttpWebRequest.Create(new Uri(String.Format("{0}://{1}/{2}", "http", ApiServerBaseUrl, first.Endpoint)));
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
                if (ex.Status == WebExceptionStatus.ProtocolError)  // This covers everything we got a valid response for (like 400 param errors)
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
                            if (errorNode != null && batch.Count() <= 1)
                            {
                                HandleApiError(errorNode.Value.ToString());
                                return;
                            }
                        }
                        catch(Newtonsoft.Json.JsonReaderException)
                        {
                            // Not an API error. Swallow and let fall through to next error handler
                        }
                    }
                }

                // Network error that we can retry?
                if (ex.Status == WebExceptionStatus.ConnectFailure ||
                    ex.Status == WebExceptionStatus.NameResolutionFailure ||
                    ex.Status == WebExceptionStatus.SecureChannelFailure ||
                    ex.Status == WebExceptionStatus.Timeout) 
                {
                    // Will just throw away if we hit the retry limit (which is fine)
                    foreach (var request in batch)
                    {
                        ScheduleRetry(request);
                    }
                }
                else
                {
                    // Calq rejects batches as a whole. If param error, then replay batch one at a time
                    if(batch.Count() > 1)
                    {
                        foreach (var request in batch)
                        {
                            MakeApiRequest(new[] { request });  
                        }
                    }
                    else
                    {
                        HandleApiError(ex.Message.ToString());
                        return;
                    }
                }
            }
        }

        #region Error handling

        /// <summary>
        ///  Delegate for handling API errors
        /// </summary>
        public delegate void CalqApiErrorHandler(string errorMessage);

        /// <summary>
        /// Triggered when an API error occurs.
        /// </summary>
        public static event CalqApiErrorHandler OnApiError;

        /// <summary>
        /// Handles errors that occurs during dispatch.
        /// </summary>
        /// <param name="errorMessage">The error message that occured.</param>
        private void HandleApiError(string errorMessage)
        {
            CalqApiErrorHandler e = OnApiError;
            if (e != null)
            {
                e(errorMessage);
            }
        }

        #endregion
    }
}
