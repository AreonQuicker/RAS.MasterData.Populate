namespace RAS.MasterData.Populate.Interfaces;

public interface IPopulateMasterDataFactoryService : IDisposable
{
    IPopulateMasterDataService<T> MakePopulateMasterDataService<T>(string autToken = null) where T : class;

    IPopulateMasterDataListService<T> MakePopulateMasterDataListService<T>(string autToken = null) where T : class;
}