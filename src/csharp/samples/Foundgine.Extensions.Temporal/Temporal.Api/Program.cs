// Wire this into Gateway.MinimalApi (Foundgine.Providers/Samples/Gateway.MinimalApi/Program.cs): it REPLACES the sample's
// hand-rolled AddJwtBearer(...) block and HttpContextSecurityExecutionContextProvider with the Keycloak integration.
using Foundgine.Extensions.Keycloak;
using Foundgine.Extensions.Temporal;

var builder = WebApplication.CreateBuilder(args);

// ... AddFoundgine(...), Postgres, event store, outbox, AddFoundgineGateway(...): as in Gateway.MinimalApi ...

builder.Services.AddFoundgineKeycloak(builder.Configuration.GetSection("Foundgine:Keycloak"))
    .AddRolePolicy("workflow-approver", "foundgine-workflow-approver");

builder.Services.AddFoundgineTemporal(o =>
    {
        o.Address = builder.Configuration["Temporal:Address"] ?? "localhost:7233";
        o.Namespace = builder.Configuration["Temporal:Namespace"] ?? "default";
        o.TaskQueue = builder.Configuration["Temporal:TaskQueue"] ?? "foundgine";
        o.UseTls = builder.Configuration.GetValue<bool>("Temporal:UseTls");
        o.ApiKey = builder.Configuration["Temporal:ApiKey"];
    })
    .AddProcess(OrderProcess.Definition)
    .AddProcess(OnboardingProcess.Definition) // same definition the worker registers; clients start it by name only
    .RouteTopic("order.created", OrderProcess.Name)
    .DispatchOutboxToTemporal();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapFoundgineTemporal(configure: o => o.ApprovalPolicy = "workflow-approver");
app.Run();
