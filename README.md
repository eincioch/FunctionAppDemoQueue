# FunctionAppDemoQueue

Proyecto de Azure Functions (.NET 8, v4) para enviar y procesar pedidos usando Azure Service Bus Queue.

Contenido principal:
- Function HTTP SendOrderToQueue: recibe JSON de un pedido y lo env�a a la cola.
- Function Service Bus ProcessOrderFromQueue: lee mensajes de la cola y los procesa.

Tecnolog�as:
- .NET 8
- Azure Functions v4 (in-proc)
- Service Bus Extension: Microsoft.Azure.WebJobs.Extensions.ServiceBus (v5.14.0)

Estructura de funciones:
- SendOrderToQueue (HTTP POST)
  - Ruta: /api/orders/send
  - Entrada: cuerpo JSON
  - Salida: mensaje en la cola definida por ServiceBusQueueName usando ServiceBusConnection
- ProcessOrderFromQueue (Service Bus Trigger)
  - Trigger: cola configurada en ServiceBusQueueName
  - Procesa el JSON y registra su contenido; si hay error, se reintenta y puede ir a la DLQ

Requisitos:
- .NET 8 SDK
- Azure Functions Core Tools v4 (para ejecutar localmente)
- Azure Service Bus (namespace y una cola existente)
- Azurite o cuenta de Azure Storage para AzureWebJobsStorage (solo en local)

Configuraci�n local (local.settings.json):
Valores requeridos en Values:
{
  "AzureWebJobsStorage": "UseDevelopmentStorage=true",
  "FUNCTIONS_INPROC_NET8_ENABLED": "1",
  "FUNCTIONS_WORKER_RUNTIME": "dotnet",
  "ServiceBusConnection": "Endpoint=sb://<tu-namespace>.servicebus.windows.net/;SharedAccessKeyName=<policy>;SharedAccessKey=<key>",
  "ServiceBusQueueName": "sbq-boleta"
}
Notas:
- No incluya EntityPath en ServiceBusConnection. La cola se indica con ServiceBusQueueName.
- Aseg�rate de que la pol�tica de acceso tenga permisos de Send y Listen seg�n corresponda.

Ejecutar localmente:
1) Restaurar y compilar
   - dotnet build
2) Iniciar Functions
   - func start
3) Endpoint disponible: http://localhost:7071/api/orders/send

Probar env�o:
Ejemplo de JSON a enviar (body del POST):
{
  "cabecera": {
    "numeroPedido": "12345",
    "fecha": "2025-09-01",
    "cliente": {
      "id": "C001",
      "nombre": "Juan P�rez",
      "direccion": "Av. Ejemplo 123, Ciudad"
    },
    "total": 150.75,
    "moneda": "USD"
  },
  "detalle": [
    { "codigoProducto": "P1001", "descripcion": "Camisa manga larga", "cantidad": 2, "precioUnitario": 35.00, "subtotal": 70.00 },
    { "codigoProducto": "P1002", "descripcion": "Pantal�n jeans", "cantidad": 1, "precioUnitario": 55.75, "subtotal": 55.75 },
    { "codigoProducto": "P1003", "descripcion": "Zapatos deportivos", "cantidad": 1, "precioUnitario": 25.00, "subtotal": 25.00 }
  ]
}
Ejemplo con curl:
- curl -X POST http://localhost:7071/api/orders/send -H "Content-Type: application/json" -d @pedido.json

Despliegue a Azure (resumen):
- Crear Function App en Azure (runtime .NET, plan y storage). 
- Configurar App Settings en la Function App:
  - ServiceBusConnection = <cadena de conexi�n sin EntityPath>
  - ServiceBusQueueName = sbq-boleta
- Publicar desde Visual Studio, Azure DevOps o GitHub Actions.

Buenas pr�cticas y seguridad:
- No commits de secretos. Use Azure App Settings y/o Azure Key Vault.
- Rotar claves de SAS regularmente.
- Supervisar DLQ de la cola para mensajes con error.

Soluci�n de problemas:
- 401 en endpoint HTTP: usar Function Key o ajustar AuthorizationLevel si aplica.
- Errores de Service Bus (autorizaci�n/conectividad): validar ServiceBusConnection y permisos de la pol�tica.
- Si no llega a la funci�n de trigger, confirmar que la cola existe y que ServiceBusQueueName coincide.

Licencia:
- MIT (o la que prefieras).
