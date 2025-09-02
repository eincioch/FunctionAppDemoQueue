using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.ServiceBus;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Logging;

namespace FunctionAppDemoQueue
{
    public static class SessionExamples
    {
        // Enviar mensaje con SessionId
        [FunctionName("SendSessionMessage")]
        public static async Task<IActionResult> Send(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sessions/send/{sessionId}")] HttpRequest req,
            string sessionId,
            [ServiceBus("%ServiceBusSessionQueueName%", Connection = "ServiceBusConnection")] IAsyncCollector<ServiceBusMessage> output,
            ILogger log)
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(body))
                return new BadRequestObjectResult("sessionId y body requeridos");

            var msg = new ServiceBusMessage(body)
            {
                SessionId = sessionId,
                ContentType = "application/json"
            };
            await output.AddAsync(msg);
            return new OkObjectResult(new { sent = true, sessionId });
        }

        // Procesar mensajes de una cola con sesiones habilitadas (usar acciones de sesión provistas por el binding)
        [FunctionName("ProcessSessionMessage")]
        public static async Task Process(
            [ServiceBusTrigger("%ServiceBusSessionQueueName%", Connection = "ServiceBusConnection", IsSessionsEnabled = true)] ServiceBusReceivedMessage message,
            ServiceBusSessionMessageActions sessionActions,
            ILogger log)
        {
            log.LogInformation("Mensaje de sesión recibido. SessionId={SessionId}, Seq={Seq}", message.SessionId, message.SequenceNumber);
            // Ejemplo de uso de acciones de sesión (opcional):
            // await sessionActions.RenewSessionLockAsync();
            await Task.CompletedTask;
        }

        // HTTP para desbloquear/cerrar explícitamente una sesión (ejemplo de administración de sesiones)
        [FunctionName("CloseSession")]
        public static async Task<IActionResult> Close(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sessions/close/{sessionId}")] HttpRequest req,
            string sessionId,
            ILogger log)
        {
            var connection = Environment.GetEnvironmentVariable("ServiceBusConnection");
            var queueName = Environment.GetEnvironmentVariable("ServiceBusSessionQueueName");
            await using var client = new ServiceBusClient(connection);
            await using var receiver = await client.AcceptSessionAsync(queueName, sessionId);
            await receiver.CloseAsync();
            return new OkObjectResult(new { closed = true, sessionId });
        }
    }
}
