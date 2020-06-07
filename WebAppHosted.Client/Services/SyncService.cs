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
    public class SyncService : ISyncService
    {
        private const string DataFileName = "data.config";
        private const string DataFileContentType = "application/json";
        private readonly IStorage _storage;
        private readonly IStorageState _storageState;
        private readonly DriveService _service;

        public SyncService(
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
        }


        public async Task Sync()
        {
            Console.WriteLine("SyncTo");
            if (_storageState.Synced)
            {
                return;
            }

            File file = await Find();
            if (file == null)
            {
                Console.WriteLine("created");
                file = await Create();
                await SyncToRemote(file);
            }
            else
            {
                VersionedData localVersionedData = await GetRemoteDataFromStorage();
                VersionedData remoteVersionedData = await Download(file);
                if (remoteVersionedData.Version > localVersionedData.Version)
                {
                    Console.WriteLine($"remote:{remoteVersionedData.Version}->local:{localVersionedData.Version}");
                    await UpdateStorageFromRemoteData(remoteVersionedData);
                }
                else if (remoteVersionedData.Version == localVersionedData.Version)
                {
                    if (await HasLocalChanges())
                    {
                        Console.WriteLine(
                            $"local:{localVersionedData.Version}->remote:{localVersionedData.Version + 1}");
                        await SyncToRemote(file, remoteVersionedData.Version + 1);
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Versions mismatch: local:{localVersionedData.Version} remote:{remoteVersionedData.Version}");
                }
            }
        }

        private async Task<bool> HasLocalChanges()
        {
            VersionedData versionedData = await GetRemoteDataFromStorage();
            VersionedData newVersionedData = await GetRemoteDataLocalData();
            bool changed = JsonConvert.SerializeObject(versionedData.Data) !=
                   JsonConvert.SerializeObject(newVersionedData.Data);

            if (!changed)
            {
                _storageState.Synced = true;
            }

            return changed;
        }

        private async Task UpdateStorageFromRemoteData(VersionedData data)
        {
            Console.WriteLine($"Synced from remote:{data.Data}");
            await _storage.SetItemAsync("remote_data", data);
            await _storage.SetItemAsync("notions", data.Data);
            _storageState.Synced = true;
        }

        private async Task<VersionedData> GetRemoteDataFromStorage(int newVersion = 0)
        {
            return await _storage.GetItemAsync<VersionedData>("remote_data") ??
                   await GetRemoteDataLocalData(newVersion);
        }

        private async Task<VersionedData> GetRemoteDataLocalData(int newVersion = 0)
        {
            var notions = await _storage.GetItemAsync<List<Notion>>("notions");
            return new VersionedData
            {
                Version = newVersion,
                Data = notions
            };
        }

        private async Task SyncToRemote(File file, int newVersion = 0)
        {
            var versionedData = await GetRemoteDataLocalData(newVersion);
            await _storage.SetItemAsync("remote_data", versionedData);
            await Upload(file, versionedData);
            var newRemoteVersionedData = await Download(file);
            await UpdateStorageFromRemoteData(newRemoteVersionedData);
            _storageState.Synced = true;
        }

        private async Task<VersionedData> Download(File file)
        {
            FilesResource.GetRequest request = _service.Files.Get(file.Id);
            using (var stream = new MemoryStream())
            {
                var rc = await request.DownloadAsync(stream);
                stream.Seek(0, SeekOrigin.Begin);
                using (var sr = new StreamReader(stream))
                {
                    string data = await sr.ReadToEndAsync();
                    Console.WriteLine(data);
                    return JsonConvert.DeserializeObject<VersionedData>(data);
                }
            }
        }

        private async Task Upload(File file, VersionedData versionedData)
        {
            var fileToUpdate = new File();
            fileToUpdate.Name = file.Name;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(versionedData))))
            {
                FilesResource.UpdateMediaUpload updateRequest =
                    _service.Files.Update(fileToUpdate, file.Id, stream, DataFileContentType);
                await updateRequest.UploadAsync();
            }
        }

        private async Task<File> Find()
        {
            var request = _service.Files.List();
            request.Spaces = "appDataFolder";
            request.Fields = "nextPageToken, files(id, name)";
            request.PageSize = 10;
            var result = await request.ExecuteAsync();
            return result.Files.FirstOrDefault(x => x.Name == DataFileName);
        }

        private async Task<File> Create()
        {
            var fileMetadata = new File()
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
                    .Create(fileMetadata, stream, DataFileContentType);
                createFileRequest.Fields = "id";
                await createFileRequest.UploadAsync();
            }

            return createFileRequest.ResponseBody;
        }

        private class VersionedData
        {
            public int Version { get; set; }
            public int FormatVersion { get; set; }
            public List<Notion> Data { get; set; }
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