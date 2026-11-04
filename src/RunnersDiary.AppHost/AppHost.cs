var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.RunnersDiary_Api>("api")
    .WithHttpHealthCheck("/health/live");

builder.AddViteApp("web", "../../web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
