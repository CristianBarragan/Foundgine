using Foundgine.Extensions.Keycloak;
using Foundgine.Extensions.Temporal;

var builder = Host.CreateApplicationBuilder(args);

// Register the application's Foundgine runtime (AddFoundgine(...)), semantic model and providers here, exactly as
// the API host does, so operations can call IFoundgineGatewayExecution / IFoundgine.

// Keycloak-backed authority for activities: warrants for WorkerRoles, bounded to the workflow's tenant.
builder.Services.AddFoundgineKeycloakWarrants(o => builder.Configuration.GetSection("Foundgine:Keycloak").Bind(o))
    .UseForTemporalWorker();

builder.Services.AddFoundgineTemporalWorker(o =>
    {
        o.Address = builder.Configuration["Temporal:Address"] ?? "localhost:7233";
        o.Namespace = builder.Configuration["Temporal:Namespace"] ?? "default";
        o.TaskQueue = builder.Configuration["Temporal:TaskQueue"] ?? "foundgine";
        o.UseTls = builder.Configuration.GetValue<bool>("Temporal:UseTls");
        o.ApiKey = builder.Configuration["Temporal:ApiKey"];
    })
    .AddProcess(OrderProcess.Definition)
    .AddProcess(OnboardingProcess.Definition)
    .AddKeycloakAdminOperations()
    .AddOperation<ValidateOrder>()
    .AddOperation<ReserveStock>()
    .AddOperation<ReleaseStock>()
    .AddOperation<NoOp>()
    .AddOperation<ChargePayment>()
    .AddOperation<RefundPayment>()
    .AddOperation<ConfirmOrder>();

// Use RunAsync, not Run (https://github.com/temporalio/sdk-dotnet/issues/220).
await builder.Build().RunAsync();
