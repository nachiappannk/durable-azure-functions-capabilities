using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;
using System.IO;
using Newtonsoft.Json;

namespace DurableAzureFunctionCapabilities
{
    public class BusBookingUseCase
    {
        [FunctionName("BookBusOrchestration")]
        public static async Task<string> BookBus([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var orchestrationRequest = context.GetInput<BookBusOrchestrationRequest>();
            var entityId = new EntityId(nameof(Bus), orchestrationRequest.BusId);
            await context.CallEntityAsync(entityId, "BookBus", new BookBusRequest()
            {
                RequestId = orchestrationRequest.RequestId,
                UserId = orchestrationRequest.UserId,
            });
            return "Done";
        }

        [FunctionName("BookBus")]
        [OpenApiOperation(operationId: "Run", tags: new[] { "book-bus" })]
        [OpenApiRequestBody("application/json", typeof(HttpBookBusRequest))]
        [OpenApiParameter(name: "busId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
        public static async Task<IActionResult> HttpBookBus(
            [HttpTrigger(AuthorizationLevel.Anonymous, new[] { "post" }, Route = "v1/buses/{busId}/book")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter, string busId)
        {
            var requestId = busId + Guid.NewGuid().ToString();
            var bodyAsString = await ReadBody(req);
            var body = JsonConvert.DeserializeObject<HttpBookBusRequest>(bodyAsString);
            string instanceId = await starter.StartNewAsync("BookBusOrchestration", requestId, new BookBusOrchestrationRequest()
            {
                RequestId = requestId,
                UserId = body.UserId,
                BusId = busId
            }).ConfigureAwait(false);

            var result = await starter.GetStatusAsync(instanceId);
            return new AcceptedResult(req.Path, new { requestId });
        }

        [FunctionName("GetBookingStatus")]
        [OpenApiOperation(operationId: "Run", tags: new[] { "booking-status" })]
        [OpenApiParameter(name: "bookingId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
        public static async Task<IActionResult> GetBookingStatus(
            [HttpTrigger(AuthorizationLevel.Anonymous, new[] { "get" }, Route = "v1/bookings/{bookingId}")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter, string bookingId)
        {
            var result = await starter.GetStatusAsync(bookingId);
            if( new[] { OrchestrationRuntimeStatus.Completed }.Contains(result.RuntimeStatus)) return new OkObjectResult("Booked");
            if (new[] { OrchestrationRuntimeStatus.Failed, OrchestrationRuntimeStatus.Canceled, OrchestrationRuntimeStatus.Terminated }.Contains(result.RuntimeStatus)) return new OkObjectResult("Booking Rejected");
            return new OkObjectResult("In progress");

        }

        private static async Task<string> ReadBody(HttpRequest req)
        {
            using (var reader = new StreamReader(req.Body))
            {
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Bus
    {
        [FunctionName(nameof(Bus))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx)
           => ctx.DispatchAsync<Bus>();


        [JsonProperty("numberOfSeats")]
        public int NumberOfSeats { get; set; } = 3;

        [JsonProperty("acceptedBookingRequests")]
        public List<BookBusRequest> AcceptedBookingRequests { get; set; } = new List<BookBusRequest>();

        public void BookBus(BookBusRequest request)
        {
            if (AcceptedBookingRequests.Count < NumberOfSeats)
            {
                AcceptedBookingRequests.Add(request);
            }
            else
            {
                throw new Exception("Seats are fully. Bus can not be booked");
            }
        }
    }

    public class BookBusRequest
    {
        public string RequestId { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;
    }

    public class HttpBookBusRequest
    {
        public string UserId { get; set; } = string.Empty;
    }

    public class BookBusOrchestrationRequest
    {
        public string BusId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
    }

}
