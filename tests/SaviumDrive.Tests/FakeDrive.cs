using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SaviumDrive.Tests
{
    /// <summary>
    /// Servidor de Drive simulado: responde a cada petición HTTP con la función indicada
    /// y guarda las peticiones recibidas para poder revisarlas en las pruebas.
    /// </summary>
    internal class FakeDriveHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        public FakeDriveHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        public static HttpResponseMessage Error(HttpStatusCode status)
        {
            int code = (int)status;
            return Json($"{{\"error\":{{\"code\":{code},\"message\":\"{status}\",\"errors\":[{{\"message\":\"{status}\",\"domain\":\"global\",\"reason\":\"{status}\"}}]}}}}", status);
        }
    }

    internal class FakeHttpClientFactory : HttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => _handler;
    }

    internal static class FakeDrive
    {
        public static DriveService CreateService(FakeDriveHandler handler)
        {
            return new DriveService(new BaseClientService.Initializer
            {
                ApplicationName = "SaviumDrive.Tests",
                HttpClientFactory = new FakeHttpClientFactory(handler),
                GZipEnabled = false
            });
        }

        /// <summary>Devuelve el valor de un parámetro de la URL ya decodificado.</summary>
        public static string QueryParam(HttpRequestMessage request, string name)
        {
            string query = request.RequestUri.Query.TrimStart('?');
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (Uri.UnescapeDataString(kv[0]) == name)
                    return kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
            }
            return null;
        }
    }
}
