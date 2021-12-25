using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

#nullable enable

namespace requestapp
{
    class NetworkEventListener : EventListener
    {
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Private.InternalDiagnostics.System.Net.Sockets")
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            var buffer = new StringBuilder(300);
            buffer.AppendFormat("EVENT: {0}/{1}", eventData.EventSource.Name, eventData.EventName).AppendLine();
            if (eventData.Payload != null && eventData.PayloadNames != null)
            {
                Debug.Assert(eventData.Payload.Count == eventData.PayloadNames.Count);
                for (int i = 0; i < eventData.PayloadNames.Count; i++)
                {
                    buffer.AppendFormat("  '{0}' = {1}", eventData.PayloadNames[i], eventData.Payload[i]).AppendLine();
                }
                Console.WriteLine(buffer.ToString());
            }
        }
    }

    class Program
    {
        private static readonly NetworkEventListener netlistener = new NetworkEventListener();

        private static readonly List<IDisposable> eventSubscriptions = new();
        private static readonly Dictionary<string, PropertyInfo> meta = new();

        static T? GetPropertyValue<T>(string eventName, string propName, object v)
        {
            var key = $"{eventName}->{propName}";
            if (!meta.TryGetValue(key, out var prop))
            {
                lock (meta)
                {
                    if (!meta.TryGetValue(key, out prop))
                    {
                        prop = v.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty)!;
                        meta.Add(key, prop);
                    }
                }
            }
            return (T?)prop.GetValue(v);
        }

        /* System.Net.Http.HttpRequestOut.Start: 'System.Net.Http.DiagnosticsHandler+ActivityStartData'
           System.Net.Http.Request: 'System.Net.Http.DiagnosticsHandler+RequestData'
           System.Net.Http.HttpRequestOut.Stop: 'System.Net.Http.DiagnosticsHandler+ActivityStopData'
           System.Net.Http.Response: 'System.Net.Http.DiagnosticsHandler+ResponseData' */
        static void SubscribeHttpHandlerEvents(KeyValuePair<string, object?> kv)
        {
            switch (kv.Key)
            {
                case "System.Net.Http.Request":
                    var req = GetPropertyValue<HttpRequestMessage>(kv.Key, "Request", kv.Value!)!;
                    Console.WriteLine($"Request: {req.Method} {req.RequestUri}");
                    break;
                case "System.Net.Http.Response":
                    var resp = GetPropertyValue<HttpResponseMessage>(kv.Key, "Response", kv.Value!);
                    Console.WriteLine($"Response: {resp?.StatusCode}");
                    break;
                default:
                    break;
            }
        }

        static async Task Main(string[] args)
        {
            var s = DiagnosticListener.AllListeners.Subscribe(
                listener => {
                    if ("HttpHandlerDiagnosticListener" == listener.Name)
                    {
                        eventSubscriptions.Add(listener.Subscribe(SubscribeHttpHandlerEvents));
                    }
                });
            eventSubscriptions.Add(s);


            // based on the recommendation by @maxfire (https://community.dotnetos.org/t/discussions-module-1-lesson-8-homework/327/25)
            // The code page 852 is required to make the bell ring
            const int CP = 852;

            // By default, .NET Core does not make available any code page encodings other than code page 28591
            // and the Unicode encodings, such as UTF-8 and UTF-16. However, you can add the code page encodings
            // found in standard Windows apps that target .NET to your app.
            var encoder = CodePagesEncodingProvider.Instance.GetEncoding(CP)!;
            Console.OutputEncoding = encoder;

            using var client = new HttpClient();

            var resp = await client.GetAsync("https://asyncexpert.com/");
            var respstr = await resp.Content.ReadAsStringAsync();

            resp = await client.GetAsync("https://asyncexpert.com/");
            respstr = await resp.Content.ReadAsStringAsync();

            Console.WriteLine($"Content length: {respstr.Length}");
        }
    }
}
