using BackupGateway.Web;
using Wkg.AspNetCore.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

WebApplication app = await builder.BuildUsingAsync<Startup>();
await app.RunAsync();
