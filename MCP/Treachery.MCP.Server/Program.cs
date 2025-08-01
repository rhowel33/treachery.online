using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Sdk;

namespace Treachery.MCP.Server;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // Add MCP server services
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly<DuneGameTools>();

        // Register our game tools
        builder.Services.AddSingleton<DuneGameTools>();

        var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Starting Dune MCP Server...");
        logger.LogInformation("Server Info:");
        logger.LogInformation("  Name: Dune Board Game MCP Server");
        logger.LogInformation("  Version: 1.0.0");
        logger.LogInformation("  Description: Provides game state access and action validation for Dune board game AI");

        try
        {
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while running the MCP server");
            Environment.Exit(1);
        }
    }
}