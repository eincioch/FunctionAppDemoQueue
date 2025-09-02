using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace FunctionAppDemoQueue
{
    public static class ListQueueMessagesFunction
    {
        // Lista mensajes de la cola mediante Peek (no los consume). Permite paginación por SequenceNumber.
        [FunctionName("ListQueueMessages")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "queue/list")] HttpRequest req,
            ILogger log)
        {
            int.TryParse(req.Query["top"], out var top);
            if (top <= 0) top = 50;
            if (top > 200) top = 200; // limitar tamaño de página

            bool deadletter = false;
            bool.TryParse(req.Query["deadletter"], out deadletter);

            long? fromSeq = null;
            if (long.TryParse(req.Query["from"], out var tmp))
            {
                fromSeq = tmp;
            }

            int.TryParse(req.Query["maxBody"], out var maxBody);
            if (maxBody < 0) maxBody = 2048; // 0 o mayor para controlar truncado; negativo usa 2048

            var connection = Environment.GetEnvironmentVariable("ServiceBusConnection");
            var queueName = Environment.GetEnvironmentVariable("ServiceBusQueueName");
            if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(queueName))
            {
                return new ObjectResult("Configuración de Service Bus no encontrada.") { StatusCode = StatusCodes.Status500InternalServerError };
            }

            await using var client = new ServiceBusClient(connection);
            var receiver = client.CreateReceiver(queueName, new ServiceBusReceiverOptions
            {
                SubQueue = deadletter ? SubQueue.DeadLetter : SubQueue.None
            });

            IReadOnlyList<ServiceBusReceivedMessage> batch;
            if (fromSeq.HasValue)
            {
                batch = await receiver.PeekMessagesAsync(top, fromSeq.Value);
            }
            else
            {
                batch = await receiver.PeekMessagesAsync(top);
            }

            var items = new List<object>(batch.Count);
            foreach (var m in batch)
            {
                var bodyText = GetBodyString(m);
                if (maxBody > 0 && bodyText.Length > maxBody) bodyText = bodyText.Substring(0, maxBody) + "...";

                items.Add(new
                {
                    sequenceNumber = m.SequenceNumber,
                    enqueuedTime = m.EnqueuedTime,
                    expiresAt = m.ExpiresAt,
                    scheduledEnqueueTime = m.ScheduledEnqueueTime,
                    lockedUntil = m.LockedUntil,
                    messageId = m.MessageId,
                    correlationId = m.CorrelationId,
                    contentType = m.ContentType,
                    subject = m.Subject,
                    applicationProperties = m.ApplicationProperties,
                    body = bodyText
                });
            }

            var next = batch.Count > 0 ? batch.Last().SequenceNumber + 1 : fromSeq;

            return new OkObjectResult(new
            {
                deadletter,
                count = items.Count,
                nextSequenceNumber = next,
                items
            });
        }

        private static string GetBodyString(ServiceBusReceivedMessage m)
        {
            try
            {
                // Intento 1: representación textual directa
                var text = m.Body.ToString();
                if (!string.IsNullOrEmpty(text)) return text;

                // Intento 2: decodificar como UTF-8 desde bytes
                var bytes = m.Body.ToArray();
                if (bytes == null || bytes.Length == 0) return string.Empty;
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "[unreadable-body]";
            }
        }
    }
}
