using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.ServiceBus;
using Newtonsoft.Json.Linq;

namespace FunctionAppDemoQueue
{
    public static class ProcessOrderFromQueueFunction
    {
        [FunctionName("ProcessOrderFromQueue")]
        public static async Task Run(
            [ServiceBusTrigger("%ServiceBusQueueName%", Connection = "ServiceBusConnection")] string message,
            ILogger log)
        {
            log.LogInformation("Service Bus queue trigger received a message.");

            if (string.IsNullOrWhiteSpace(message))
            {
                log.LogWarning("Mensaje vacío recibido.");
                return;
            }

            try
            {
                var json = JToken.Parse(message);
                // Aquí puedes implementar la lógica de negocio. Por ahora solo se registra el contenido.


                log.LogInformation("Pedido procesado: {Contenido}", json.ToString());
            }
            catch (Exception ex)
            {
                // Lanzar excepción hará que el runtime reprograme el mensaje y, tras los reintentos, vaya a la DLQ.
                log.LogError(ex, "Error procesando el mensaje de la cola. Contenido: {Contenido}", message);
                throw;
            }

            await Task.CompletedTask;
        }
    }
}
