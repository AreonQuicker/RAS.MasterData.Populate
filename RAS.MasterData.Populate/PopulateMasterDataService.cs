using System.Collections.Concurrent;
using System.Reflection;
using E.S.ApiClientHandler.Config;
using E.S.ApiClientHandler.Core;
using E.S.ApiClientHandler.Interfaces;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using RAS.MasterData.Attributes;
using RAS.MasterData.Helpers;
using RAS.MasterData.Populate.Interfaces;

namespace RAS.MasterData.Populate;

public class PopulateMasterDataService<T> : IPopulateMasterDataService<T> where T : class
{
    private const int _cashTimeInSeconds = 7200;
    private readonly IApiCachingManager _apiCachingManager;
    private readonly HttpClient _httpClient;
    private readonly MasterDataConfig _masterDataConfig;

    #region Constructror

    public PopulateMasterDataService(MasterDataConfig masterDataConfig, IHttpContextAccessor httpContextAccessor,
        HttpClient httpClient, IApiCachingManager apiCachingManager, string autToken = null)
    {
        _propertyWithAttributes = new List<(PropertyInfo Property, BaseAttribute[] Attributes)>();
        _propertyWithNonFieldAttributes =
            new List<(PropertyInfo Property, (string Key, BaseAttribute Attribute)[] Attributes)>();
        _propertyWithFieldAttributes =
            new List<(PropertyInfo Property, (string Key, MasterDataFieldAttribute Attribute)[] Attributes)>();
        _values = new ConcurrentDictionary<string, ConcurrentDictionary<string, JObject>>();
        _batchValues = new ConcurrentDictionary<string, List<JObject>>();
        _masterDataConfig = masterDataConfig;
        _httpClient = httpClient;
        _apiCachingManager = apiCachingManager;
        _authToken = autToken ?? httpContextAccessor?.HttpContext?.Request?.Headers["Authorization"];
    }

    #endregion

    #region Fields

    private string _authToken;
    private T _item;
    private bool _getByBatches;

    #endregion

    #region Private Fields

    private List<(PropertyInfo Property, BaseAttribute[] Attributes)> _propertyWithAttributes;

    private List<(PropertyInfo Property, (string Key, BaseAttribute Attribute)[] Attributes)>
        _propertyWithNonFieldAttributes;

    private List<(PropertyInfo Property, (string Key, MasterDataFieldAttribute Attribute)[] Attributes)>
        _propertyWithFieldAttributes;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, JObject>> _values;
    private readonly ConcurrentDictionary<string, List<JObject>> _batchValues;

    #endregion

    #region IPopulateMasterDataService

    public IPopulateMasterDataService<T> Clear()
    {
        _propertyWithAttributes.Clear();
        _propertyWithNonFieldAttributes.Clear();
        _propertyWithFieldAttributes.Clear();
        _values.Clear();
        _batchValues.Clear();
        _authToken = null;
        _item = null;

        return this;
    }

    public IPopulateMasterDataService<T> WithItem(T item)
    {
        _item = item;

        return this;
    }

    public IPopulateMasterDataService<T> WithAuthToken(string authToken)
    {
        _authToken = authToken;

        return this;
    }

    public IPopulateMasterDataService<T> GetByBatches()
    {
        _getByBatches = true;

        return this;
    }

    public async Task<T> PopulateAsync(T item = null)
    {
        if (item != null)
            _item = item;

        SetPropertiesAndAttributes();

        await SetBatchValuesAsync();
        await GetValuesAsync();

        SetValues();

        return _item;
    }

    public void SetPropertiesAndAttributes()
    {
        GetPropertyWithAttributes();
        GetPropertyWithNonFieldAttributes();
        GetPropertyWithFieldAttributes();
    }

    public async Task SetBatchValuesAsync()
    {
        if (_getByBatches)
            await GatBatchValuesAsync();
    }

    public void Dispose()
    {
        Clear();
    }

    #endregion

    #region Private Methods

    private void GetPropertyWithAttributes()
    {
        if (_propertyWithAttributes.Any())
            return;

        _propertyWithAttributes = _item.GetType()
            .GetProperties()
            .Select(s => (s, (BaseAttribute[])s.GetCustomAttributes(typeof(BaseAttribute), true)))
            .Where(w => w.Item2.Any())
            .ToList();
    }

    private void GetPropertyWithNonFieldAttributes()
    {
        if (_propertyWithNonFieldAttributes.Any())
            return;

        _propertyWithNonFieldAttributes = _propertyWithAttributes
            .Select(s => (s.Property,
                s.Attributes.GroupBy(g => $"{g.GetDataSet()}*{g.GetDataGroup()}")
                    .Select(ss => (ss.Key,
                        ss.FirstOrDefault(f => f is MasterDataCodeAttribute) ??
                        ss.FirstOrDefault(f => f is MasterDataIdAttribute) ??
                        ss.FirstOrDefault(f => f is MasterDataCustomPropertyAttribute)))
                    .Where(w => w.Item2 != null)
                    .ToArray()))
            .Where(w => w.Item2.Any())
            .ToList();
    }

    private void GetPropertyWithFieldAttributes()
    {
        if (_propertyWithFieldAttributes.Any())
            return;

        _propertyWithFieldAttributes = _propertyWithAttributes
            .Select(s => (s.Property,
                s.Attributes.Where(w => w is MasterDataFieldAttribute)
                    .GroupBy(g => $"{g.GetDataSet()}*{g.GetDataGroup()}")
                    .Select(ss => (ss.Key,
                        (MasterDataFieldAttribute)ss.FirstOrDefault(f => f is MasterDataFieldAttribute)))
                    .Where(w => w.Item2 != null)
                    .ToArray()))
            .Where(w => w.Item2.Any())
            .ToList();
    }

    private async Task GatBatchValuesAsync()
    {
        if (_batchValues.Any())
            return;

        await Parallel.ForEachAsync(_propertyWithNonFieldAttributes, new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (properyWithAttribute, cancel) =>
            {
                foreach (var attribute in properyWithAttribute.Attributes)
                {
                    var dataSet = attribute.Attribute.GetDataSet();

                    if (!_batchValues.ContainsKey(attribute.Key))
                    {
                        var values = await GetMasterDataListAsync(dataSet);

                        if (values == null || !values.Any())
                            continue;

                        _batchValues.TryAdd(attribute.Key, values);
                    }
                }
            });
    }

    private async Task GetValuesAsync()
    {
        _values.Clear();
        foreach (var properyWithAttribute in _propertyWithNonFieldAttributes) await GetValueAsync(properyWithAttribute);
    }

    private async Task GetValueAsync(
        (PropertyInfo Property, (string Key, BaseAttribute Attribute)[] Attributes) properyWithAttribute)
    {
        foreach (var attribute in properyWithAttribute.Attributes)
            if (attribute.Attribute is MasterDataCodeAttribute codeAttribute)
            {
                var propertyValue = properyWithAttribute.Property?.GetValue(_item)?.ToString()?.Trim();

                if (string.IsNullOrEmpty(propertyValue))
                    continue;

                _values.TryGetValue(attribute.Key, out var d);

                if (d?.ContainsKey(propertyValue) ?? false)
                    continue;

                var dataSet = codeAttribute.GetDataSet();

                var value = await GetItemByCodeAsync(attribute.Key, dataSet,
                    propertyValue);

                if (value == null)
                    continue;

                AddValue(d, attribute.Key,
                    propertyValue, value);
            }
            else if (attribute.Attribute is MasterDataIdAttribute idAttribute)
            {
                var propertyValue = properyWithAttribute.Property?.GetValue(_item)?.ToString()?.Trim();

                if (string.IsNullOrEmpty(propertyValue))
                    continue;

                _values.TryGetValue(attribute.Key, out var d);

                if (d?.ContainsKey(propertyValue) ?? false)
                    continue;

                var dataSet = idAttribute.GetDataSet();

                var value = await GetItemByIdAsyncAsync(attribute.Key, dataSet,
                    int.Parse(propertyValue));

                if (value == null)
                    continue;

                AddValue(d, attribute.Key,
                    propertyValue, value);
            }
            else if (attribute.Attribute is MasterDataCustomPropertyAttribute customPropertyAttribute)
            {
                var customName = customPropertyAttribute.GetCustomPropertyName();
                var propertyValue = properyWithAttribute.Property?.GetValue(_item)?.ToString()?.Trim();

                if (string.IsNullOrEmpty(propertyValue) || string.IsNullOrEmpty(customName))
                    continue;

                var propertyKey = propertyValue + "*" + customName;

                _values.TryGetValue(attribute.Key, out var d);

                if (d?.ContainsKey(propertyKey) ?? false)
                    continue;

                var dataSet = customPropertyAttribute.GetDataSet();

                var value = await GetItemByCustomPropertyAsync(attribute.Key, dataSet,
                    customName, propertyValue);

                if (value == null)
                    continue;

                AddValue(d, attribute.Key,
                    propertyKey, value);
            }
    }

    private void AddValue(ConcurrentDictionary<string, JObject> d, string key, string propertyKey, JObject value)
    {
        if (d == null)
        {
            d = new ConcurrentDictionary<string, JObject>();
            _values.TryAdd(key, d);
        }

        if (!d.ContainsKey(propertyKey))
            d.TryAdd(propertyKey, value);
    }

    private void SetValues()
    {
        foreach (var properyWithAttribute in _propertyWithFieldAttributes)
        foreach (var attribute in properyWithAttribute.Attributes)
        {
            var fieldName = attribute.Attribute.GetFieldName();

            _values.TryGetValue(attribute.Key, out var masterIemValues);

            if (masterIemValues == null)
                continue;

            var masterIemValue = masterIemValues?.Values.FirstOrDefault();

            if (masterIemValue == null)
                continue;

            var value = masterIemValue.Children()
                .FirstOrDefault(
                    t => string.Equals(t.Path, fieldName,
                        StringComparison.CurrentCultureIgnoreCase))
                ?.Children()
                .First()
                .ToString();

            if (string.IsNullOrWhiteSpace(value))
                continue;

            var valueType = properyWithAttribute.Property.PropertyType;

            try
            {
                var convertedValue = Convert.ChangeType(value, valueType);

                properyWithAttribute.Property.SetValue(_item, convertedValue);
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task<JObject> GetItemByIdAsyncAsync(string key, string masterDataSetName, int id)
    {
        if (_batchValues.TryGetValue(key, out var value))
            return value?.FirstOrDefault(jobj => jobj["id"].Value<int>() == id);

        return await GetMasterDataItemByIdAsyncAsync(masterDataSetName, id);
    }

    private async Task<JObject> GetMasterDataItemByIdAsyncAsync(string masterDataSetName, int id)
    {
        try
        {
            if (_batchValues.TryGetValue(masterDataSetName, out var value))
                return value?.FirstOrDefault(jobj => jobj["id"].Value<int>() == id);

            var result = await ApiRequestBuilder
                .Make(_httpClient, _masterDataConfig.MasterDataServiceUrl,
                    ApiRequestBuilderConfig.Create())
                .WithCacheClient(_apiCachingManager, _cashTimeInSeconds)
                .New()
                .WithMethod(HttpMethod.Get)
                .AddHeader("Authorization", _authToken)
                .WithCache(true)
                .WithUrl($"{masterDataSetName}/getbyid/{id}")
                .ExecuteAsync<JObject>();

            return result.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<JObject> GetItemByCodeAsync(string key, string masterDataSetName, string code)
    {
        if (_batchValues.TryGetValue(key, out var value))
            return value?.FirstOrDefault(jobj => jobj["code"].Value<string>() == code);

        return await GetMasterDataItemByCodeAsync(masterDataSetName, code);
    }

    private async Task<JObject> GetMasterDataItemByCodeAsync(string masterDataSetName, string code)
    {
        try
        {
            if (_batchValues.TryGetValue(masterDataSetName, out var value))
                return value?.FirstOrDefault(jobj => jobj["code"].Value<string>() == code);

            var result = await ApiRequestBuilder
                .Make(_httpClient, _masterDataConfig.MasterDataServiceUrl,
                    ApiRequestBuilderConfig.Create())
                .WithCacheClient(_apiCachingManager, _cashTimeInSeconds)
                .New()
                .WithMethod(HttpMethod.Get)
                .WithCache(true)
                .AddHeader("Authorization", _authToken)
                .WithUrl($"{masterDataSetName}/{code}")
                .ExecuteAsync<JObject>();

            return result.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<JObject> GetItemByCustomPropertyAsync(string key, string masterDataSetName,
        string propertyName, string propertyValue)
    {
        if (_batchValues.TryGetValue(key, out var value))
            return value?.FirstOrDefault(jobj => jobj[propertyName]?.Value<string>() == propertyValue);

        return await GetMasterDataItemByCustomPropertyAsync(masterDataSetName, propertyName,
            propertyValue);
    }

    private async Task<JObject> GetMasterDataItemByCustomPropertyAsync(string masterDataSetName,
        string propertyName, string propertyValue)
    {
        try
        {
            if (_batchValues.TryGetValue(masterDataSetName, out var value))
                return value?.FirstOrDefault(jobj => jobj[propertyName].Value<string>() == propertyValue);

            var result = await ApiRequestBuilder
                .Make(_httpClient, _masterDataConfig.MasterDataServiceUrl,
                    ApiRequestBuilderConfig.Create())
                .WithCacheClient(_apiCachingManager, _cashTimeInSeconds)
                .New()
                .WithMethod(HttpMethod.Get)
                .AddHeader("Authorization", _authToken)
                .WithCache(true)
                .WithUrl($"{masterDataSetName}")
                .ExecuteAsync<List<JObject>>();

            return result.Value?.FirstOrDefault(b => b.GetValue(propertyName).ToString() == propertyValue);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<JObject>> GetMasterDataListAsync(string dataSet)
    {
        try
        {
            var result = await ApiRequestBuilder
                .Make(_httpClient, _masterDataConfig.MasterDataServiceUrl,
                    ApiRequestBuilderConfig.Create())
                .WithCacheClient(_apiCachingManager, _cashTimeInSeconds)
                .New()
                .WithMethod(HttpMethod.Get)
                .AddHeader("Authorization", _authToken)
                .WithCache(true)
                .WithUrl($"{dataSet}")
                .ExecuteAsync<List<JObject>>();

            return result.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    #endregion
}