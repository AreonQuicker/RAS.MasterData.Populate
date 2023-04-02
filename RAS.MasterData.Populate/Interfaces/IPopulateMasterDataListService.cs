namespace RAS.MasterData.Populate.Interfaces;

public interface IPopulateMasterDataListService<T> : IDisposable
    where T : class
{
    IPopulateMasterDataListService<T> WithItems(List<T> items);
    Task<IList<T>> PopulateAsync();
}