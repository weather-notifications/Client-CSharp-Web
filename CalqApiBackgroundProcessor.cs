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
using System.Web.Hosting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Calq.Client.Web
{
    /// <summary>
    /// Processes requests to call API end points. 
    /// </summary>
    public class CalqApiBackgroundProcessor : IRegisteredObject
    { 
        /// <summary>
        /// The max number of retries for failed API calls.
        /// </summary>
        public const int MaxRetries = 16;

        /// <summary>
        /// The delay between retrying events (This increases per retry on each event)
        /// </summary>
        public const double InitialRetryDelaySeconds = 60;
        
        /// <summary>
        /// Queue of outgoing requests to API end points that have previously failed.
        /// </summary>
        internal LinkedList<CalqRequest> RetryQueue = new LinkedList<CalqRequest>();

        /// <summary>
        /// Lock to create workers.
        /// </summary>
        protected object CreationLock = new object();

        /// <summary>
        /// The retry worker used to replay failed requests.
        /// </summary>
        protected Task RetryWorker;

        /// <summary>
        /// Flag asking our worker to abort when it can.
        /// </summary>
        protected bool StopFlag = false;

        /// <summary>
        /// Flag asking our worker to abort NOW.
        /// </summary>
        protected bool StopImmediateFlag = false;

        /// <summary>
        /// Singleton background processor instance.
        /// </summary>
        protected static Lazy<CalqApiBackgroundProcessor> Singleton = new Lazy<CalqApiBackgroundProcessor>();

        /// <summary>
        /// Creates a new background API processor to handle failed calls in background. Called by Lazy<T> only.
        /// </summary>
        protected CalqApiBackgroundProcessor()
        {
            HostingEnvironment.RegisterObject(this);
        }

        /// <summary>
        /// Called when the hosting environment needs us to stop.
        /// </summary>
        /// <param name="immediate">Whether we have a few seconds time to clean up, or if its STOP NOW ZOMG time.</param>
        void IRegisteredObject.Stop(bool immediate)
        {
            StopFlag = true;
            StopImmediateFlag = immediate;
            if(RetryWorker != null && RetryWorker.Status == TaskStatus.Running)
            {
                if(immediate)
                {
                    RetryWorker.Wait(20 * 1000);
                }
            }
        }

        /// <summary>
        /// Schedules retrying on the given request.
        /// </summary>
        /// <param name="request">The request to retry later.</param>
        internal static void ScheduleRetry(CalqRequest request)
        {
            if (request.Retries < CalqApiBackgroundProcessor.MaxRetries)
            {
                var singleton = Singleton.Value;

                lock (singleton.RetryQueue)
                {
                    request.NextRetry = DateTime.Now.AddSeconds(InitialRetryDelaySeconds * Math.Pow(1.5, request.Retries + 1));
                    singleton.RetryQueue.AddLast(request);
                }

                lock (singleton.CreationLock)
                {
                    // Spool up bg timer if needed (Still using RetryQueue as lock)
                    if (singleton.RetryWorker == null || singleton.RetryWorker.IsCanceled || singleton.RetryWorker.IsCompleted || singleton.RetryWorker.IsFaulted)
                    {
                        singleton.RetryWorker = Task.Factory.StartNew(() => { singleton.RetryWorkerCoreLoop(); });
                    }
                }
            }
        }

        /// <summary>
        /// Replays failed requests.
        /// </summary>
        private void RetryWorkerCoreLoop()
        {
            while(!StopFlag)
            {
                var queue = new Queue<CalqRequest>();
                lock (RetryQueue)
                {
                    // TODO: At some point swap this LL implementation for a priority queue. Going to be more efficient
                    var node = RetryQueue.First;
                    var now = DateTime.Now;
                    while (node != null)
                    {
                        var next = node.Next;
                        if (node.Value.NextRetry < now)
                        {
                            node.List.Remove(node); // Node ref, so doesn't walk
                            queue.Enqueue(node.Value);
                        }
                        node = next;
                    }
                }

                while (queue.Count > 0 && !StopImmediateFlag)
                {
                    var request = queue.Dequeue();
                    request.Retries++;
                    try
                    {
                        CalqApiProcessor.MakeApiPost(request);
                    }
                    catch (Exception)
                    {
                        // Just swallow. These have already failed and will likely fail again. Original context is gone, so not much can do
                    }
                }

                // Emptied the queue?
                lock (RetryQueue)
                {
                    if (RetryQueue.Count == 0 || StopImmediateFlag)
                    {
                        return;
                    }
                }

                // We flushed everything we could. Check again in a little
                Thread.Sleep(10 * 1000);
            }

        }
    }
}
