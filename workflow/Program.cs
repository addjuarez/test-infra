﻿using Dapr.Client;
using Dapr.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using WorkflowConsoleApp.Activities;
using WorkflowConsoleApp.Models;
using WorkflowConsoleApp.Workflows;

const string StoreName = "statestore";
const string DaprWorkflowComponent = "dapr";

// The workflow host is a background service that connects to the sidecar over gRPC
var builder = Host.CreateDefaultBuilder(args).ConfigureServices(services =>
{
    services.AddDaprWorkflow(options =>
    {
        // Note that it's also possible to register a lambda function as the workflow
        // or activity implementation instead of a class.
        options.RegisterWorkflow<OrderProcessingWorkflow>();

        // These are the activities that get invoked by the workflow(s).
        options.RegisterActivity<NotifyActivity>();
        options.RegisterActivity<ReserveInventoryActivity>();
        options.RegisterActivity<ProcessPaymentActivity>();
        options.RegisterActivity<UpdateInventoryActivity>();
    });
});

// Start the app - this is the point where we connect to the Dapr sidecar
using var host = builder.Build();
host.Start();

using var daprClient = new DaprClientBuilder().Build();

// NOTE: WorkflowEngineClient will be replaced with a richer version of DaprClient
//       in a subsequent SDK release. This is a temporary workaround.
WorkflowEngineClient workflowClient = host.Services.GetRequiredService<WorkflowEngineClient>();

// Generate a unique ID for the workflow
string orderId = Guid.NewGuid().ToString()[..8];
string itemToPurchase = "Cars";
int ammountToPurchase = 10;

// Populate the store with items
RestockInventory(itemToPurchase);

// Construct the order
OrderPayload orderInfo = new OrderPayload(itemToPurchase, 15000, ammountToPurchase);

// Start the workflow
Console.WriteLine("Starting workflow {0} purchasing {1} {2}", orderId, ammountToPurchase, itemToPurchase);

await daprClient.StartWorkflowAsync(
    workflowComponent: DaprWorkflowComponent,
    workflowName: nameof(OrderProcessingWorkflow),
    input: orderInfo,
    instanceId: orderId);

// Wait for the workflow to start and confirm the input
GetWorkflowResponse state = await daprClient.WaitForWorkflowStartAsync(
    instanceId: orderId,
    workflowComponent: DaprWorkflowComponent);

Console.WriteLine("Your workflow has started. Here is the status of the workflow: {0}", state.RuntimeStatus);

// Wait for the workflow to complete
state = await daprClient.WaitForWorkflowCompletionAsync(
    instanceId: orderId,
    workflowComponent: DaprWorkflowComponent);

Console.WriteLine("Workflow Status: {0}", state.RuntimeStatus);

void RestockInventory(string itemToPurchase)
{
    daprClient.SaveStateAsync<OrderPayload>(StoreName, itemToPurchase,  new OrderPayload(Name: itemToPurchase, TotalCost: 15000, Quantity: 100));
    return;
}
