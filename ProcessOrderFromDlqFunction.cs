using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.ServiceBus;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FunctionAppDemoQueue
{
    public static class ProcessOrderFromDlqFunction
    {
        [FunctionName("ProcessOrderFromDlq")]
        public static async Task Run(
            [ServiceBusTrigger("%ServiceBusQueueName%/$DeadLetterQueue", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
            ILogger log)
        {
            log.LogWarning("DLQ trigger: mensaje recibido desde la Dead-Letter Queue.");

            string body = message.Body.ToString();

            message.ApplicationProperties.TryGetValue("DeadLetterReason", out var reasonObj);
            message.ApplicationProperties.TryGetValue("DeadLetterErrorDescription", out var descObj);
            var reason = reasonObj?.ToString();
            var description = descObj?.ToString();

            try
            {
                JToken? json = null;
                try { json = JToken.Parse(body); } catch { /* no JSON */ }

                log.LogWarning("DLQ info - MessageId: {MessageId}, Sequence: {Seq}, Reason: {Reason}, Description: {Desc}",
                    message.MessageId, message.SequenceNumber, reason, description);

                if (json != null)
                {
                    var numeroPedido = json.SelectToken("cabecera.numeroPedido")?.ToString();
                    log.LogInformation("Contenido JSON en DLQ. numeroPedido: {NumeroPedido}, Body: {Body}", numeroPedido, json.ToString());
                }
                else
                {
                    log.LogInformation("Contenido texto en DLQ. Body: {Body}", body);
                }

                // TODO: implementar remediación: corregir, reenviar, archivar, etc.
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                // Dejar que reintente en DLQ (se volverá a entregar). Considera límites de reintentos del host.
                log.LogError(ex, "Error procesando mensaje en DLQ. MessageId: {MessageId}", message.MessageId);
                throw;
            }
        }
    }
}
