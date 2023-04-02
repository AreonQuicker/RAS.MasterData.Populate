using E.S.ApiClientHandler.Interfaces;
using Microsoft.AspNetCore.Http;
using RAS.MasterData.Helpers;
using RAS.MasterData.Populate.Interfaces;

namespace RAS.MasterData.Populate;

public class PopulateMasterDataListService<T> : IPopulateMasterDataListService<T>
    where T : class
{
    private readonly IApiCachingManager _apiCachingManager;
    private readonly string _autToken;
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly MasterDataConfig _masterDataConfig;

    #region Fields

    private List<T> _items;

    #endregion

    public PopulateMasterDataListService(MasterDataConfig masterDataConfig,
        IHttpContextAccessor httpContextAccessor,
        HttpClient httpClient,
        IApiCachingManager apiCachingManager,
        string autToken = null)
    {
        _masterDataConfig = masterDataConfig;
        _httpContextAccessor = httpContextAccessor;
        _httpClient = httpClient;
        _apiCachingManager = apiCachingManager;
        _autToken = autToken;
    }

    #region IPopulateMasterDataListService

    public IPopulateMasterDataListService<T> WithItems(List<T> items)
    {
        _items = items;

        return this;
    }

    public async Task<IList<T>> PopulateAsync()
    {
        if (!_items.Any()) return _items;

        var populateMasterDataService = new PopulateMasterDataService<T>(
            _masterDataConfig,
            _httpContextAccessor,
            _httpClient,
            _apiCachingManager,
            _autToken);

        populateMasterDataService.GetByBatches();
        populateMasterDataService.WithItem(_items.First());
        populateMasterDataService.SetPropertiesAndAttributes();
        await populateMasterDataService.SetBatchValuesAsync();

        foreach (var item in _items) await populateMasterDataService.PopulateAsync(item);

        populateMasterDataService.Dispose();

        return _items;
    }

    public void Dispose()
    {
        _items.Clear();
    }

    #endregion
}