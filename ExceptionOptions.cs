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
using System.Linq;
using System.Text;

namespace Calq.Client.Web
{
    public enum ExceptionOptions
    {
        /// <summary>
        /// Swallow all exceptions. Application will continue as if no API exceptioned occurred.
        /// </summary>
        None,

        /// <summary>
        /// Report only API exceptions (where the server has indicated a problem with the API call - such as bad data).
        /// Will queue and attempt to retry actions where there has been a connection issue. Recommended for production.
        /// </summary>
        ThrowApiExceptions,

        /// <summary>
        /// Throw any exception encountered. This could include network issues connecting to API server. This is the default.
        /// </summary>
        ThrowAllExceptions
    }
}
