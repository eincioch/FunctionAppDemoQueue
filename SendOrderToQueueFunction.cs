using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.ServiceBus;
using Newtonsoft.Json.Linq;

namespace FunctionAppDemoQueue
{
    public static class SendOrderToQueueFunction
    {
        [FunctionName("SendOrderToQueue")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders/send")] HttpRequest req,
            [ServiceBus("%ServiceBusQueueName%", Connection = "ServiceBusConnection")] IAsyncCollector<string> outputMessages,
            ILogger log)
        {
            log.LogInformation("HTTP trigger received order payload to enqueue to Service Bus.");

            string body = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                return new BadRequestObjectResult("Se requiere un cuerpo JSON.");
            }

            // Validate that body is valid JSON
            try
            {
                JToken.Parse(body);
            }
            catch
            {
                return new BadRequestObjectResult("JSON inválido en el cuerpo de la solicitud.");
            }

            await outputMessages.AddAsync(body);

            return new OkObjectResult("Mensaje enviado a la cola de Service Bus.");
        }
    }
}
