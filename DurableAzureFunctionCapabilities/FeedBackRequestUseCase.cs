﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System;
using System.Threading;

namespace DurableAzureFunctionCapabilities
{
    public class FeedBackRequestUseCase
    {
        [FunctionName("CreateFeedbackRequest")]
        [OpenApiOperation(operationId: "Run", tags: new[] { "feedback" })]
        public static async Task<IActionResult> GetBookingStatus(
            [HttpTrigger(AuthorizationLevel.Anonymous, new[] { "get" }, Route = "v1/feebacks")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            var bodyAsString = await ReadBody(req);
            var body = JsonConvert.DeserializeObject<HttpFeedbackRequest>(bodyAsString);

            var result = await starter.GetStatusAsync(bookingId);
            if (new[] { OrchestrationRuntimeStatus.Completed }.Contains(result.RuntimeStatus)) return new OkObjectResult("Booked");
            if (new[] { OrchestrationRuntimeStatus.Failed, OrchestrationRuntimeStatus.Canceled, OrchestrationRuntimeStatus.Terminated }.Contains(result.RuntimeStatus)) return new OkObjectResult("Booking Rejected");
            return new OkObjectResult("In progress");

        }

        [FunctionName("FeedbackOrchestrationOrchestration")]
        public static async Task<string> CreateFeedback([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var orchestrationRequest = context.GetInput<CreateFeedbackOrchestrationParameters>();
            var requestId = await context.CallActivityAsync<string>("CreateFeedbackActivity", new CreateFeedbackActityParameters() { });
            var requestTime = context.CurrentUtcDateTime;
            var timeForNextCheck = requestTime;
            while (context.CurrentUtcDateTime < requestTime.AddDays(30))
            {
                timeForNextCheck = timeForNextCheck.AddDays(2);
                await context.CreateTimer(timeForNextCheck, CancellationToken.None);
                var isFeebackProvided = await context.CallActivityAsync<bool>("IsFeedbackProvider", requestId);
                if (isFeebackProvided == true) return "Feedback provided";
            }
            await context.CallActivityAsync("CancelFeedbackRequest", requestId);
            return "Feedback not provided. Feedback request cancelled";
        }

        [FunctionName("CreateFeedbackActivity")]
        public async Task<string> CreateFeedbackActity([ActivityTrigger] CreateFeedbackActityParameters parameters)
        {
            var requestId = Guid.NewGuid().ToString();
            //todo implementation of creating feedback request
            return requestId;
        }

        [FunctionName("IsFeedbackProvider")]
        public async Task<bool> IsFeedbackProvider([ActivityTrigger] string requestId)
        {
            //todo implementation of checking feedback request
            return false;
        }

        [FunctionName("CancelFeedbackRequest")]
        public async Task CancelFeedbackRequest([ActivityTrigger] string requestId)
        {
        }

        private static async Task<string> ReadBody(HttpRequest req)
        {
            using (var reader = new StreamReader(req.Body))
            {
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }
    }

    public class HttpFeedbackRequest
    { 
        public string RequestorId { get; set; }

        public string RequestHandlerid { get; set; }
    }

    public class CreateFeedbackOrchestrationParameters
    { 
    }

    public class CreateFeedbackActityParameters
    { 
    }
}
