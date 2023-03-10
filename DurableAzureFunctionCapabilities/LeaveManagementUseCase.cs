using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.OpenApi.Models;

namespace DurableAzureFunctionCapabilities
{
    public class LeaveManagementUseCase
    {
        [FunctionName("CreateLeaveRequest")]
        [OpenApiOperation(operationId: "Run", tags: new[] { "leave" })]
        [OpenApiRequestBody("application/json", typeof(HttpLeaveRequest))]
        public static async Task<IActionResult> CreateLeave(
            [HttpTrigger(AuthorizationLevel.Anonymous, new[] { "post" }, Route = "v1/leaves")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            var bodyAsString = await ReadBody(req);
            var body = JsonConvert.DeserializeObject<HttpLeaveRequest>(bodyAsString);
            var guid = Guid.NewGuid().ToString();
            var requestId = await starter.StartNewAsync("CreateLeaveOrchestration", guid, new CreateLeaveOrchestrationParameters() 
            { 
                UserId = body.UserId,
                EndDate = body.EndDate,
                StartDate = body.StartDate,
            });
            return new AcceptedResult(req.Path, requestId);
        }

        [FunctionName("ApproveLeave")]
        [OpenApiOperation(operationId: "Run", tags: new[] { "leave" })]
        [OpenApiParameter(name: "requestId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
        public static async Task<IActionResult> ApproveLeave(
            [HttpTrigger(AuthorizationLevel.Anonymous, new[] { "post" }, Route = "v1/leaves/{requestId}/approve")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter, string requestId)
        {
            await starter.RaiseEventAsync(requestId, "Approved");
            return new OkResult();
        }

        [FunctionName("DeclineLeave")]
        [OpenApiOperation(operationId: "Run", tags: new[] { "leave" })]
        [OpenApiParameter(name: "requestId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
        public static async Task<IActionResult> DeclineLeave(
            [HttpTrigger(AuthorizationLevel.Anonymous, new[] { "post" }, Route = "v1/leaves/{requestId}/decline")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter, string requestId)
        {
            await starter.RaiseEventAsync(requestId, "Denied");
            return new OkResult();
        }

        [FunctionName("CreateLeaveOrchestration")]
        public static async Task<string> CreatLeaveOrchestration([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var orchestrationRequest = context.GetInput<CreateLeaveOrchestrationParameters>();
            var leaveId = await context.CallActivityAsync<string>("CreateLeaveRequetActivity", new CreateLeaveRequestActivityParameters()
            {
                UserId = orchestrationRequest.UserId,
                EndDate = orchestrationRequest.EndDate,
                StartDate = orchestrationRequest.StartDate,
            });

            var approvalTask = context.WaitForExternalEvent("Approved");
            var declinedTask = context.WaitForExternalEvent("Denied");
            var timerTask = context.CreateTimer(context.CurrentUtcDateTime.AddDays(1), CancellationToken.None);
            await Task.WhenAny(approvalTask, declinedTask, timerTask);

            if (declinedTask.IsCompleted)
            {
                await UpdateRequest(context, leaveId, false, "Denied");
                return "Leave declined";
            }
            if (approvalTask.IsCompleted)
            {
                await UpdateRequest(context, leaveId, true, "Manager approved");
                return "Leave Approved";
            }
            await UpdateRequest(context, leaveId, true, "Auto approved");
            return "Leave auto approved";
        }

        private static async Task UpdateRequest(IDurableOrchestrationContext context, string requestId, bool isApproved, string additionalInfo)
        {
            await context.CallActivityAsync<string>("UpdateLeaveRequestActivity", new CreateLeaveUpdateActivityparameters()
            {
                RequestId = requestId,
                IsApproved = isApproved,
                AdditionalInfo = additionalInfo
            });
        }

        [FunctionName("CreateLeaveRequestActivity")]
        public async Task<string> CreateLeaveRequestActivity([ActivityTrigger] CreateLeaveRequestActivityParameters parameters)
        {
            var requestId = Guid.NewGuid().ToString();
            //todo implementation of creating leave request
            return requestId;
        }

        [FunctionName("UpdateLeaveRequestActivity")]
        public async Task UpdateLeaveRequestActivity([ActivityTrigger] CreateLeaveUpdateActivityparameters parameters)
        {
            var requestId = Guid.NewGuid().ToString();
            //todo implementation of updating leave request
        }

        private static async Task<string> ReadBody(HttpRequest req)
        {
            using (var reader = new StreamReader(req.Body))
            {
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }
    }

    public class HttpLeaveRequest
    {
        public string UserId { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }
    }


    public class CreateLeaveOrchestrationParameters
    {
        public string UserId { get; set; }
        
        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }
    }

    public class CreateLeaveRequestActivityParameters
    {
        public string UserId { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }
    }

    public class CreateLeaveUpdateActivityparameters
    {
        public string RequestId { get; set; }

        public bool IsApproved { get; set; }

        public string AdditionalInfo { get; set; }
    }
}
