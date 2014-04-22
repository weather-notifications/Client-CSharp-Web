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
using System.Linq;
using System.Text;

namespace Calq.Client.Web
{
    public class ReservedApiProperties
    {
        /// <summary>
        /// The name of a new action.
        /// </summary>
        public const string ActionName = "action_name";

        /// <summary>
        /// The unique actor of an event (e.g. a user id, server name, etc).
        /// </summary>
        public const string Actor = "actor";

        /// <summary>
        /// The source ip address for this action.
        /// </summary>
        public const string IpAddress = "ip_address";

        /// <summary>
        /// The previous unique id of an actor when transfering.
        /// </summary>
        public const string OldActor = "old_actor";

        /// <summary>
        /// The new unique id of an actor when transfering.
        /// </summary>
        public const string NewActor = "new_actor";

        /// <summary>
        /// Properties node giving user provided custom information.
        /// </summary>
        public const string UserProperties = "properties";

        /// <summary>
        /// The timestamp of a new event.
        /// </summary>
        public const string Timestamp = "timestamp";

        /// <summary>
        /// The unique key to identify this project when writing.
        /// </summary>
        public const string ProjectWriteKey = "write_key";

    }
}
