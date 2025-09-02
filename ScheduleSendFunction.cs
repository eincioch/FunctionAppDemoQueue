using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.ServiceBus;
using Microsoft.Extensions.Logging;

namespace FunctionAppDemoQueue
{
    public static class ScheduleSendFunction
    {
        [FunctionName("ScheduleSend")] 
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "queue/schedule")] HttpRequest req,
            [ServiceBus("%ServiceBusQueueName%", Connection = "ServiceBusConnection")] IAsyncCollector<ServiceBusMessage> output,
            ILogger log)
        {
            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                return new BadRequestObjectResult("Body requerido");
            }
            if (!int.TryParse(req.Query["scheduleInSeconds"], out var seconds) || seconds <= 0)
            {
                return new BadRequestObjectResult("Proporcione scheduleInSeconds>0");
            }

            var msg = new ServiceBusMessage(body) { ContentType = "application/json" };
            var connection = Environment.GetEnvironmentVariable("ServiceBusConnection");
            var queueName = Environment.GetEnvironmentVariable("ServiceBusQueueName");
            await using var client = new ServiceBusClient(connection);
            var sender = client.CreateSender(queueName);
            var when = DateTimeOffset.UtcNow.AddSeconds(seconds);
            await sender.ScheduleMessageAsync(msg, when);

            return new OkObjectResult(new { scheduled = true, scheduledEnqueueTime = when });
        }
    }
}
