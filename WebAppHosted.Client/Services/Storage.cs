using System.Threading.Tasks;
using Blazored.LocalStorage;

namespace WebAppHosted.Client.Services
{
    public class Storage : IStorage
    {
        private readonly ILocalStorageService _localStorage;
        private readonly IStorageState _storageState;

        public Storage(ILocalStorageService localStorage, IStorageState storageState)
        {
            _localStorage = localStorage;
            _storageState = storageState;
        }


        public Task<T> GetItemAsync<T>(string key)
        {
            return _localStorage.GetItemAsync<T>(key);
        }

        public Task SetItemAsync<T>(string key, T data)
        {
            _storageState.Synced = false;
            return _localStorage.SetItemAsync(key, data);
        }
    }
}