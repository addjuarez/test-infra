// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// ------------------------------------------------------------

namespace WorkflowGen
{
    using Dapr.Client;
    using Dapr.Tests.Common.Models;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Hosting;
    using Prometheus;
    using System;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using WorkflowGen.Activities;
    using WorkflowGen.Models;
    using WorkflowGen.Workflows;
    /// <summary>
    /// FeedGenerator - generates messages and publishes them using Dapr.
    /// The main functionality is in StartMessageGeneratorAsync().
    /// </summary>
    public class Program
    {
        private static readonly Gauge PublishCallTime = Metrics.CreateGauge("lh_workflow_generator_publish_call_time", "The time it takes for the workflow call to return");

        private static readonly Counter PublishFailureCount = Metrics.CreateCounter("lh_workflow_generator_publish_failure_count", "Publich calls that throw");

        const string DaprWorkflowComponent = "dapr";


        /// <summary>
        /// Main for FeedGenerator
        /// </summary>
        /// <param name="args">Arguments.</param>
        public static void Main(string[] args)
        {
            int delayInMilliseconds = 10000;

            var server = new MetricServer(port: 9988);
            server.Start();

            IHost host = CreateHostBuilder(args).Build();

            Task.Run(() => StartMessageGeneratorAsync(delayInMilliseconds));

            host.Run();
        }

        /// <summary>
        /// Creates WebHost Builder.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>Returns IHostbuilder.</returns>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        static internal async void StartMessageGeneratorAsync(int delayInMilliseconds)
        {

            TimeSpan delay = TimeSpan.FromMilliseconds(delayInMilliseconds);

            DaprClientBuilder daprClientBuilder = new DaprClientBuilder();

            DaprClient client = daprClientBuilder.Build();
            var Counter = 0;
            while (true)
            {
                Random random = new Random();
                var num = random.Next(30);
                OrderPayload orderInfo = new OrderPayload("Cars", num * 1000, num);
                string orderId = Guid.NewGuid().ToString()[..8];

                try
                {
                    Console.WriteLine("Publishing");
                    using (PublishCallTime.NewTimer())
                    {

                        await client.StartWorkflowAsync(
                            workflowComponent: DaprWorkflowComponent,
                            workflowName: nameof(OrderProcessingWorkflow),
                            input: orderInfo,
                            instanceId: orderId);

                            GetWorkflowResponse state = await client.WaitForWorkflowStartAsync(
                                instanceId: orderId,
                                workflowComponent: DaprWorkflowComponent);

                            Console.WriteLine("Your workflow has started. Here is the status of the workflow: {0}", state.RuntimeStatus);

                            state = await client.WaitForWorkflowCompletionAsync(
                                instanceId: orderId,
                                workflowComponent: DaprWorkflowComponent);

                            Console.WriteLine("Workflow Status: {0}", state.RuntimeStatus);

                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Caught {0}", e.ToString());
                    PublishFailureCount.Inc();
                }

                Counter++;
                if (Counter > 5){
                    await client.SaveStateAsync<OrderPayload>("statestore", "cars",  new OrderPayload(Name: "cars", TotalCost: 15000, Quantity: 100));
                    Counter = 0;
                }
                await Task.Delay(delay);
            }
        }
    }
}
