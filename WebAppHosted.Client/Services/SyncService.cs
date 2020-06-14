using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WebAppHosted.Client.Models;

namespace WebAppHosted.Client.Services
{
    public class SyncService : ISyncService
    {
        private readonly IStorage _storage;
        private readonly IStorageState _storageState;
        private readonly IRemoteStorage _remoteStorage;

        public SyncService(IRemoteStorage remoteStorage, IStorageState storageState, IStorage storage)
        {
            _remoteStorage = remoteStorage;
            _storageState = storageState;
            _storage = storage;
        }

        public async Task Sync()
        {
            await InitStorageState();
            Console.WriteLine("SyncTo");
            if (_storageState.Synced == true)
            {
                return;
            }

            VersionedData cachedRemoteData = await GetCachedRemoteData();
            VersionedData remoteData = await _remoteStorage.GetRemoteData() ??
                                       await SyncToRemoteStorage(cachedRemoteData.Version);
            if (remoteData.Version > cachedRemoteData.Version)
            {
                Console.WriteLine($"remote:{remoteData.Version}->local:{cachedRemoteData.Version}");
                await UpdateStorageFromRemoteData(remoteData);
            }
            else if (remoteData.Version == cachedRemoteData.Version)
            {
                await SyncToRemoteStorage(remoteData.Version + 1);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Versions mismatch: local:{cachedRemoteData.Version} remote:{remoteData.Version}");
            }
        }

        private async Task InitStorageState()
        {
            _storageState.Synced ??= JsonConvert.SerializeObject((await GetVersionedDataFromLocalStorage()).Data) ==
                                     JsonConvert.SerializeObject((await GetCachedRemoteData()).Data);
        }

        private async Task<VersionedData> GetCachedRemoteData(int newVersion = 0)
        {
            return await _storage.GetItemAsync<VersionedData>("remote_data") ??
                   await GetVersionedDataFromLocalStorage(newVersion);
        }

        private async Task<VersionedData> GetVersionedDataFromLocalStorage(int newVersion = 0)
        {
            var notions = await _storage.GetItemAsync<List<Notion>>("notions");
            return new VersionedData
            {
                Version = newVersion,
                Data = notions
            };
        }

        private async Task UpdateStorageFromRemoteData(VersionedData data)
        {
            Console.WriteLine($"Synced from remote:{data.Data}");
            await _storage.SetItemAsync("remote_data", data);
            await _storage.SetItemAsync("notions", data.Data);
            _storageState.Synced = true;
        }

        private async Task<VersionedData> SyncToRemoteStorage(int newVersion)
        {
            VersionedData newRemoteData = await GetVersionedDataFromLocalStorage(newVersion);
            await _storage.SetItemAsync("remote_data", newRemoteData);
            await _remoteStorage.Upload(newRemoteData);
            VersionedData remoteData = await _remoteStorage.GetRemoteData();
            await UpdateStorageFromRemoteData(remoteData);
            _storageState.Synced = true;

            return remoteData;
        }
    }
}