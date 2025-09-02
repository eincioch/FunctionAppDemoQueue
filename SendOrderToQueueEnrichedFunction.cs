using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.ServiceBus;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FunctionAppDemoQueue
{
    public static class SendOrderToQueueEnrichedFunction
    {
        [FunctionName("SendOrderToQueueEnriched")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders/send-enriched")] HttpRequest req,
            [ServiceBus("%ServiceBusQueueName%", Connection = "ServiceBusConnection")] IAsyncCollector<ServiceBusMessage> output,
            ILogger log)
        {
            log.LogInformation("HTTP trigger received order payload to enqueue to Service Bus (enriched).");

            string body = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                return new BadRequestObjectResult("Se requiere un cuerpo JSON.");
            }

            // Validate JSON and extract numeroPedido
            JToken parsed;
            try { parsed = JToken.Parse(body); }
            catch { return new BadRequestObjectResult("JSON inválido en el cuerpo de la solicitud."); }

            var numeroPedido = parsed.SelectToken("cabecera.numeroPedido")?.ToString();
            if (string.IsNullOrWhiteSpace(numeroPedido))
                return new BadRequestObjectResult("El JSON debe incluir cabecera.numeroPedido.");

            var msg = new ServiceBusMessage(body)
            {
                ContentType = "application/json",
                Subject = "Pedido",
                MessageId = numeroPedido,
                CorrelationId = numeroPedido
            };
            msg.ApplicationProperties["numeroPedido"] = numeroPedido;
            msg.ApplicationProperties["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("o");
            msg.ApplicationProperties["createdAtLocal"] = DateTimeOffset.Now.ToString("o");

            // Optional TTL from query: ttlSeconds>0 => sets TimeToLive; if absent, use queue default TTL
            if (int.TryParse(req.Query["ttlSeconds"], out var ttlSeconds) && ttlSeconds > 0)
            {
                msg.TimeToLive = TimeSpan.FromSeconds(ttlSeconds);
            }

            // Optional scheduling from query: scheduleInSeconds>0 => schedule in the future using SDK (binder won't schedule automatically)
            DateTimeOffset? scheduledFor = null;
            if (int.TryParse(req.Query["scheduleInSeconds"], out var scheduleInSeconds) && scheduleInSeconds > 0)
            {
                scheduledFor = DateTimeOffset.UtcNow.AddSeconds(scheduleInSeconds);
                var connection = Environment.GetEnvironmentVariable("ServiceBusConnection");
                var queueName = Environment.GetEnvironmentVariable("ServiceBusQueueName");
                if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(queueName))
                {
                    return new ObjectResult("Configuración de Service Bus no encontrada para programar el mensaje.") { StatusCode = StatusCodes.Status500InternalServerError };
                }

                await using var client = new ServiceBusClient(connection);
                ServiceBusSender sender = client.CreateSender(queueName);
                await sender.ScheduleMessageAsync(msg, scheduledFor.Value);

                return new OkObjectResult(new
                {
                    message = "Mensaje enriquecido programado.",
                    messageId = numeroPedido,
                    scheduledEnqueueTime = scheduledFor,
                    ttlSeconds = (msg.TimeToLive > TimeSpan.Zero) ? (int?)msg.TimeToLive.TotalSeconds : null
                });
            }

            // Immediate send via binding
            await output.AddAsync(msg);
            return new OkObjectResult(new
            {
                message = "Mensaje enriquecido enviado.",
                messageId = numeroPedido,
                ttlSeconds = (msg.TimeToLive > TimeSpan.Zero) ? (int?)msg.TimeToLive.TotalSeconds : null
            });
        }
    }
}
