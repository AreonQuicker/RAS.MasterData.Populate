using E.S.ApiClientHandler.Interfaces;
using Microsoft.AspNetCore.Http;
using RAS.MasterData.Helpers;
using RAS.MasterData.Populate.Interfaces;

namespace RAS.MasterData.Populate;

public class PopulateMasterDataFactoryService : IPopulateMasterDataFactoryService
{
    private readonly IApiCachingManager _apiCachingManager;
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly MasterDataConfig _masterDataConfig;

    #region Constructror

    public PopulateMasterDataFactoryService(MasterDataConfig masterDataConfig,
        IHttpContextAccessor httpContextAccessor,
        IApiCachingManager apiCachingManager)
    {
        _masterDataConfig = masterDataConfig;
        _httpContextAccessor = httpContextAccessor;
        _apiCachingManager = apiCachingManager;
        _httpClient = new HttpClient();
    }

    #endregion

    #region IPopulateMasterDataFactoryService

    public IPopulateMasterDataService<T> MakePopulateMasterDataService<T>(string autToken = null) where T : class
    {
        return new PopulateMasterDataService<T>(_masterDataConfig, _httpContextAccessor, _httpClient,
            _apiCachingManager, autToken);
    }

    public IPopulateMasterDataListService<T> MakePopulateMasterDataListService<T>(string autToken = null)
        where T : class
    {
        return new PopulateMasterDataListService<T>(_masterDataConfig, _httpContextAccessor, _httpClient,
            _apiCachingManager, autToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    #endregion
}