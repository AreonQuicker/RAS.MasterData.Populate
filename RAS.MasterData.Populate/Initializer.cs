using E.S.ApiClientHandler.Interfaces;
using E.S.ApiClientHandler.Managers;
using E.S.Simple.MemoryCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RAS.MasterData.Helpers;
using RAS.MasterData.Populate.Interfaces;

namespace RAS.MasterData.Populate;

public static class Initializer
{
    public static void AddMasterDataPopulate(this IServiceCollection services, IConfiguration configuration)
    {
        var masterDataConfig = new MasterDataConfig
        {
            MasterDataServiceUrl =
                configuration[
                    $"MasterDataConfig2:MasterDataServiceUrl:{Environment.GetEnvironmentVariable("APPSTORE_RUNTIME_ENV") ?? "local"}"],
            LoggingEnabled = bool.Parse(configuration["MasterDataConfig:LoggingEnabled"] ?? "false")
        };

        services.AddMasterDataPopulate(masterDataConfig);
    }

    public static void AddMasterDataPopulate(this IServiceCollection services, MasterDataConfig configuration)
    {
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IApiCachingManager, ApiMemoryCachingManager>();
        services.AddSimpleMemoryCache();
        services.AddSingleton<MasterDataConfig, MasterDataConfig>(_ => configuration);
        services.AddScoped<IPopulateMasterDataFactoryService, PopulateMasterDataFactoryService>();
    }
}