var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.SocialMediaBot>("socialmediabot");

builder.Build().Run();
