Calq C# Web Client
=================

The full quick start and reference docs can be found at: https://www.calq.io/docs/client/csharp-web

Installation
------------

Grab the [latest compiled release](https://github.com/Calq/Client-CSharp-Web/releases) and add it to your project.

The client will need your Calq write key to send events. You can find this inside the Calq reporting interface. The easiest way to give Calq your write key is to add it to your `web.config` inside the `appSettings` section.  Add a new node with your write key called `CalqWriteKey`.

```xml
<configuration>
    <appSettings>
        <add key="CalqWriteKey" value="YOUR_WRITE_KEY_HERE" />
        <!-- ... other entries ... -->
    </appSettings>
</configuration>
```

Getting a client instance
-------------------------

The easiest way to get an instance of the client is to use the static `CalqClient.FromCurrentRequest` method. This will create a client using any cookie already data set for the current web request. If the current user has never been seen before the client will remember them in future by writing a cookie to the response.

```c#
// Get an instance populated from the current request
var client = CalqClient.FromCurrentRequest();
```

The C# Web client is compatible with the JavaScript client. Any properties set client side using JavaScript will be read by the C# client when using the `CalqClient.FromCurrentRequest` method. Likewise any properties set server side will be persisted to a cookie to be read browser side.

Tracking actions
----------------

Calq performs analytics based on actions that user's take. You can track an action using `CalqClient.Track`. Specify the action and any associated data you wish to record.

```c#
// Track a new action called "Product Review" with a custom rating
var extras = new Dictionary<string, object>();
extras.Add("Rating", 9.0);
client.Track("Product Review", extras);
```

The dictionary parameter allows you to send additional custom data about the action. This extra data can be used to make advanced queries within Calq.

Documentation
-------------

The full quick start can be found at: https://www.calq.io/docs/client/csharp-web

The reference can be found at:  https://www.calq.io/docs/client/csharp-web/reference

License
--------

[Licensed under the Apache License, Version 2.0](http://www.apache.org/licenses/LICENSE-2.0).





