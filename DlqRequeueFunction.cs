using System;
using System.Collections.Generic;
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
    public static class DlqRequeueFunction
    {
        // Reenvía (copia) un mensaje desde la DLQ a la cola activa buscando por messageId o numeroPedido.
        // Nota: Por simplicidad, NO elimina el original de la DLQ.
        [FunctionName("DlqRequeue")] 
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "dlq/requeue")] HttpRequest req,
            ILogger log)
        {
            var messageId = req.Query["messageId"].ToString();
            var numeroPedido = req.Query["numeroPedido"].ToString();
            int.TryParse(req.Query["max"], out var maxToScan);
            if (maxToScan <= 0) maxToScan = 500;

            if (string.IsNullOrWhiteSpace(messageId) && string.IsNullOrWhiteSpace(numeroPedido))
            {
                return new BadRequestObjectResult("Proporcione messageId o numeroPedido");
            }

            var connection = Environment.GetEnvironmentVariable("ServiceBusConnection");
            var queueName = Environment.GetEnvironmentVariable("ServiceBusQueueName");
            if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(queueName))
            {
                return new ObjectResult("Configuración de Service Bus no encontrada.") { StatusCode = StatusCodes.Status500InternalServerError };
            }

            await using var client = new ServiceBusClient(connection);
            var dlq = client.CreateReceiver(queueName, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
            var sender = client.CreateSender(queueName);

            long? from = null;
            int remaining = maxToScan;
            while (remaining > 0)
            {
                int batchSize = Math.Min(remaining, 50);
                IReadOnlyList<ServiceBusReceivedMessage> peeked = from.HasValue
                    ? await dlq.PeekMessagesAsync(batchSize, from.Value)
                    : await dlq.PeekMessagesAsync(batchSize);

                if (peeked.Count == 0) break;

                foreach (var m in peeked)
                {
                    bool match = false;
                    if (!string.IsNullOrWhiteSpace(messageId) && string.Equals(m.MessageId, messageId, StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(numeroPedido))
                    {
                        if (m.ApplicationProperties != null && m.ApplicationProperties.TryGetValue("numeroPedido", out var prop) &&
                            string.Equals(Convert.ToString(prop), numeroPedido, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                        }
                        else
                        {
                            try
                            {
                                var token = JToken.Parse(m.Body.ToString());
                                var np = token.SelectToken("cabecera.numeroPedido")?.ToString();
                                match = !string.IsNullOrEmpty(np) && string.Equals(np, numeroPedido, StringComparison.OrdinalIgnoreCase);
                            }
                            catch { }
                        }
                    }

                    if (match)
                    {
                        var clone = new ServiceBusMessage(m.Body)
                        {
                            ContentType = m.ContentType,
                            CorrelationId = m.CorrelationId,
                            MessageId = m.MessageId,
                            Subject = m.Subject,
                            SessionId = m.SessionId
                        };
                        foreach (var kv in m.ApplicationProperties)
                        {
                            clone.ApplicationProperties[kv.Key] = kv.Value;
                        }

                        await sender.SendMessageAsync(clone);

                        return new OkObjectResult(new
                        {
                            requeued = true,
                            messageId = m.MessageId,
                            sequenceNumber = m.SequenceNumber,
                            sessionId = m.SessionId
                        });
                    }
                }

                from = peeked[^1].SequenceNumber + 1;
                remaining -= peeked.Count;
            }

            return new NotFoundObjectResult("Mensaje no encontrado en DLQ dentro del límite de escaneo.");
        }
    }
}
