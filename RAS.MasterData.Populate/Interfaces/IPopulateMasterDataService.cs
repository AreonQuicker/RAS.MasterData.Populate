namespace RAS.MasterData.Populate.Interfaces;

public interface IPopulateMasterDataService<T> : IDisposable
    where T : class
{
    IPopulateMasterDataService<T> Clear();
    IPopulateMasterDataService<T> WithItem(T item);
    IPopulateMasterDataService<T> WithAuthToken(string authToken);
    IPopulateMasterDataService<T> GetByBatches();
    void SetPropertiesAndAttributes();
    Task SetBatchValuesAsync();
    Task<T> PopulateAsync(T item = null);
}