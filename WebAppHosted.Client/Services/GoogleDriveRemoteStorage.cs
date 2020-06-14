using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Newtonsoft.Json;
using WebAppHosted.Client.Models;
using File = Google.Apis.Drive.v3.Data.File;

namespace WebAppHosted.Client.Services
{
    public class GoogleDriveRemoteStorage : IRemoteStorage
    {
        private const string DataFileName = "data.config";
        private const string DataFileContentType = "application/json";
        private readonly IStorage _storage;
        private readonly IStorageState _storageState;
        private readonly DriveService _service;
        private readonly RemoteStorageInfoRepository _remoteStorageInfoRepository;

        public GoogleDriveRemoteStorage(
            IAccessTokenProvider accessTokenProvider,
            IStorage storage,
            IStorageState storageState)
        {
            _storage = storage;
            _storageState = storageState;
            GoogleApiAccessTokenProvider googleApiAccessTokenProvider =
                new GoogleApiAccessTokenProvider(accessTokenProvider);

            _service = new DriveService(new BaseClientService.Initializer()
            {
                GZipEnabled = false, // for prevent exceptions for gzipped content
                HttpClientInitializer = googleApiAccessTokenProvider
            });
            _remoteStorageInfoRepository = new RemoteStorageInfoRepository(_service);
        }

        public async Task Sync()
        {
            Console.WriteLine("SyncTo");
            if (_storageState.Synced)
            {
                return;
            }

            VersionedData cachedRemoteData = await GetCachedRemoteData();
            VersionedData remoteData = await GetRemoteData();
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

        private async Task UpdateStorageFromRemoteData(VersionedData data)
        {
            Console.WriteLine($"Synced from remote:{data.Data}");
            await _storage.SetItemAsync("remote_data", data);
            await _storage.SetItemAsync("notions", data.Data);
            _storageState.Synced = true;
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

        private async Task<VersionedData> SyncToRemoteStorage(int newVersion = 0)
        {
            VersionedData newRemoteData = await GetVersionedDataFromLocalStorage(newVersion);
            await _storage.SetItemAsync("remote_data", newRemoteData);
            await Upload(newRemoteData);
            VersionedData remoteData = await GetRemoteData();
            await UpdateStorageFromRemoteData(remoteData);
            _storageState.Synced = true;

            return remoteData;
        }

        private async Task<VersionedData> GetRemoteData()
        {
            var remoteStorageInfo = await _remoteStorageInfoRepository.GetRemoteStorageInfo();
            FilesResource.GetRequest request = _service.Files.Get(remoteStorageInfo.Id);
            VersionedData remoteData;
            using (var stream = new MemoryStream())
            {
                var rc = await request.DownloadAsync(stream);
                stream.Seek(0, SeekOrigin.Begin);
                using (var sr = new StreamReader(stream))
                {
                    string data = await sr.ReadToEndAsync();
                    Console.WriteLine(data);
                     remoteData = JsonConvert.DeserializeObject<VersionedData>(data);
                }
            }

            return remoteData ?? await SyncToRemoteStorage();
        }

        private async Task Upload(VersionedData data)
        {
            var storageToUpdate = new File {Name = (await _remoteStorageInfoRepository.GetRemoteStorageInfo()).Name};
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data))))
            {
                FilesResource.UpdateMediaUpload updateRequest =
                    _service.Files.Update(storageToUpdate,
                        (await _remoteStorageInfoRepository.GetRemoteStorageInfo()).Id, stream, DataFileContentType);
                await updateRequest.UploadAsync();
            }
        }

        private class VersionedData
        {
            public int Version { get; set; }
            public int FormatVersion { get; set; }
            public List<Notion> Data { get; set; }
        }

        private class RemoteStorageInfoRepository
        {
            private readonly DriveService _service;
            private File _remoteStorageInfo;

            public RemoteStorageInfoRepository(DriveService service)
            {
                _service = service;
            }

            public async Task<File> GetRemoteStorageInfo()
            {
                File remoteStorageInfo = await FetchRemoteStorageInfo();
                if (remoteStorageInfo == null)
                {
                    Console.WriteLine("created");
                    remoteStorageInfo = await CreateRemoteStorage();
                }

                return remoteStorageInfo;
            }

            private async Task<File> FetchRemoteStorageInfo()
            {
                if (_remoteStorageInfo != null)
                {
                    return _remoteStorageInfo;
                }

                var request = _service.Files.List();
                request.Spaces = "appDataFolder";
                request.Fields = "nextPageToken, files(id, name)";
                request.PageSize = 10;
                var result = await request.ExecuteAsync();
                _remoteStorageInfo = result.Files.FirstOrDefault(x => x.Name == DataFileName);
                return _remoteStorageInfo;
            }

            private async Task<File> CreateRemoteStorage()
            {
                var remoteStorageInfo = new File()
                {
                    Name = DataFileName,
                    Parents = new List<string>()
                    {
                        "appDataFolder"
                    }
                };
                FilesResource.CreateMediaUpload createFileRequest;
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Empty)))
                {
                    createFileRequest = _service.Files
                        .Create(remoteStorageInfo, stream, DataFileContentType);
                    createFileRequest.Fields = "id";
                    await createFileRequest.UploadAsync();
                }

                return createFileRequest.ResponseBody;
            }
        }

        private class GoogleApiAccessTokenProvider : IConfigurableHttpClientInitializer, IHttpExecuteInterceptor
        {
            private readonly BearerToken.AuthorizationHeaderAccessMethod _accessMethod =
                new BearerToken.AuthorizationHeaderAccessMethod();

            private readonly IAccessTokenProvider _accessTokenProvider;

            public GoogleApiAccessTokenProvider(IAccessTokenProvider accessTokenProvider)
            {
                _accessTokenProvider = accessTokenProvider;
            }

            public void Initialize(ConfigurableHttpClient httpClient) =>
                httpClient.MessageHandler.Credential = this;

            public async Task InterceptAsync(HttpRequestMessage request, CancellationToken taskCancellationToken)
            {
                var tokenResult = await _accessTokenProvider.RequestAccessToken(
                    new AccessTokenRequestOptions
                    {
                        Scopes = new[] {"https://www.googleapis.com/auth/drive.appdata"}
                    });
                if (tokenResult.TryGetToken(out AccessToken token))
                {
                    Console.WriteLine(
                        $"token:[ expires: {token.Expires} scopes: {string.Join("|", token.GrantedScopes)}");

                    _accessMethod.Intercept(request, token.Value);
                }
            }
        }
    }
}