using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);

_ = builder.AddAzureContainerAppEnvironment("container-apps");

var foundryIdentity = builder.AddAzureUserAssignedIdentity("foundry-identity");

var foundry = builder.AddBicepTemplate("foundry", "infra/foundry.bicep")
    .WithParameter("appPrincipalId", foundryIdentity.Resource.PrincipalId);

var api = builder.AddProject<Projects.Helpdesk_Api>("api");

var web = builder.AddProject<Projects.Helpdesk_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithAzureUserAssignedIdentity(foundryIdentity)
    .WaitFor(foundry)
    .WithEnvironment("Agent__ProjectEndpoint", foundry.GetOutput("projectEndpoint"))
    .WithEnvironment("Agent__ModelDeploymentName", foundry.GetOutput("modelDeploymentName"))
    .WithEnvironment("AZURE_CLIENT_ID", foundryIdentity.Resource.ClientId);

builder.Build().Run();
