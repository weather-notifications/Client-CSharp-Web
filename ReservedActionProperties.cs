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
    public class ReservedActionProperties
    {
        public const string SaleValue = "$sale_value";
        public const string SaleCurrency = "$sale_currency";

        public const string DeviceAgent = "$device_agent";
        public const string DeviceOs = "$device_os";
        public const string DeviceResolution = "$device_resolution";
        public const string DeviceMobile = "$device_mobile";

        public const string Country = "$country";
        public const string Region = "$region";
        public const string City = "$city";

        public const string Gender = "$gender";
        public const string Age = "$age";

    }
}
