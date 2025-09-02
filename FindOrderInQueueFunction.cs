using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FunctionAppDemoQueue
{
    public static class FindOrderInQueueFunction
    {
        // Busca un numeroPedido dentro de los mensajes de la cola haciendo peek (no consume los mensajes)
        [FunctionName("FindOrderInQueue")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "orders/find/{numeroPedido}")] HttpRequest req,
            string numeroPedido,
            ILogger log)
        {
            if (string.IsNullOrWhiteSpace(numeroPedido))
            {
                return new BadRequestObjectResult("numeroPedido requerido");
            }

            int.TryParse(req.Query["max"], out var maxToScan);
            if (maxToScan <= 0) maxToScan = 500; // límite por defecto para escaneo

            bool deadletter = false;
            bool.TryParse(req.Query["deadletter"], out deadletter);

            var connection = Environment.GetEnvironmentVariable("ServiceBusConnection");
            var queueName = Environment.GetEnvironmentVariable("ServiceBusQueueName");
            if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(queueName))
            {
                return new ObjectResult("Configuración de Service Bus no encontrada.") { StatusCode = StatusCodes.Status500InternalServerError };
            }

            await using var client = new ServiceBusClient(connection);

            var receiverOptions = new ServiceBusReceiverOptions
            {
                SubQueue = deadletter ? SubQueue.DeadLetter : SubQueue.None
            };

            ServiceBusReceiver receiver = client.CreateReceiver(queueName, receiverOptions);

            long? fromSequenceNumber = null;
            int remaining = maxToScan;

            while (remaining > 0)
            {
                int batchSize = Math.Min(remaining, 50);
                IReadOnlyList<ServiceBusReceivedMessage> messages;
                if (fromSequenceNumber.HasValue)
                {
                    messages = await receiver.PeekMessagesAsync(batchSize, fromSequenceNumber.Value);
                }
                else
                {
                    messages = await receiver.PeekMessagesAsync(batchSize);
                }

                if (messages == null || messages.Count == 0)
                {
                    break; // no hay más mensajes para inspeccionar
                }

                foreach (var m in messages)
                {
                    // 1) Intentar en Application Properties
                    if (m.ApplicationProperties != null &&
                        m.ApplicationProperties.TryGetValue("numeroPedido", out var propVal) &&
                        string.Equals(Convert.ToString(propVal), numeroPedido, StringComparison.OrdinalIgnoreCase))
                    {
                        return OkResult(m, deadletter);
                    }

                    // 2) Intentar parsear el cuerpo JSON y comparar cabecera.numeroPedido
                    try
                    {
                        var bodyText = m.Body.ToString();
                        var token = JToken.Parse(bodyText);
                        var np = token.SelectToken("cabecera.numeroPedido")?.ToString();
                        if (!string.IsNullOrEmpty(np) && string.Equals(np, numeroPedido, StringComparison.OrdinalIgnoreCase))
                        {
                            return OkResult(m, deadletter, token);
                        }
                    }
                    catch
                    {
                        // ignorar mensajes que no sean JSON
                    }
                }

                fromSequenceNumber = messages.Last().SequenceNumber + 1;
                remaining -= messages.Count;
            }

            return new NotFoundObjectResult($"numeroPedido {numeroPedido} no encontrado en {(deadletter ? "DLQ" : "cola")} (inspeccionados hasta {maxToScan} mensajes)");
        }

        private static IActionResult OkResult(ServiceBusReceivedMessage m, bool deadletter, JToken? parsed = null)
        {
            var result = new
            {
                found = true,
                location = deadletter ? "deadletter" : "active",
                sequenceNumber = m.SequenceNumber,
                enqueuedTime = m.EnqueuedTime,
                expiresAt = m.ExpiresAt,
                scheduledEnqueueTime = m.ScheduledEnqueueTime,
                lockedUntil = m.LockedUntil,
                messageId = m.MessageId,
                correlationId = m.CorrelationId,
                applicationProperties = m.ApplicationProperties,
                body = parsed?.ToString() ?? m.Body.ToString()
            };
            return new OkObjectResult(result);
        }
    }
}
