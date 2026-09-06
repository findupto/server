using Microsoft.Extensions.Hosting.WindowsServices;

namespace FindUpTo.Pos.Server.Services;

public static class WindowsServiceExtensions
{
    public static IHostBuilder ConfigurePosWindowsService(this IHostBuilder host)
        => host.UseWindowsService(options => options.ServiceName = "FindUpTo POS Server");
}
