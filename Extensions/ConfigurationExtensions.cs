namespace DndMcpAICsharpFun.Extensions;

internal static class ConfigurationExtensions
{
    internal static WebApplicationBuilder AddDndConfiguration(this WebApplicationBuilder builder)
    {
        builder.Configuration
            .AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"Config/appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
        return builder;
    }
}