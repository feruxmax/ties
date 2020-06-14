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
using File = Google.Apis.Drive.v3.Data.File;

namespace WebAppHosted.Client.Services
{
    public class GoogleDriveRemoteStorage : IRemoteStorage
    {
        private const string DataFileName = "data.config";
        private const string DataFileContentType = "application/json";
        private readonly IStorage _storage;
        private readonly DriveService _service;
        private readonly RemoteStorageInfoRepository _remoteStorageInfoRepository;

        public GoogleDriveRemoteStorage(
            IAccessTokenProvider accessTokenProvider,
            IStorage storage)
        {
            _storage = storage;
            GoogleApiAccessTokenProvider googleApiAccessTokenProvider =
                new GoogleApiAccessTokenProvider(accessTokenProvider);

            _service = new DriveService(new BaseClientService.Initializer()
            {
                GZipEnabled = false, // for prevent exceptions for gzipped content
                HttpClientInitializer = googleApiAccessTokenProvider
            });
            _remoteStorageInfoRepository = new RemoteStorageInfoRepository(_service);
        }

        public async Task<VersionedData> GetRemoteData()
        {
            var remoteStorageInfo = await _remoteStorageInfoRepository.GetRemoteStorageInfo();
            FilesResource.GetRequest request = _service.Files.Get(remoteStorageInfo.Id);
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

        public async Task Upload(VersionedData data)
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

        private class RemoteStorageInfoRepository
        {
            private readonly DriveService _service;
            private File? _remoteStorageInfo;

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
                _remoteStorageInfo = result.Files.FirstOrDefault(x => x.Name == GoogleDriveRemoteStorage.DataFileName);
                return _remoteStorageInfo;
            }

            private async Task<File> CreateRemoteStorage()
            {
                var remoteStorageInfo = new File()
                {
                    Name = GoogleDriveRemoteStorage.DataFileName,
                    Parents = new List<string>()
                    {
                        "appDataFolder"
                    }
                };
                FilesResource.CreateMediaUpload createFileRequest;
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Empty)))
                {
                    createFileRequest = _service.Files
                        .Create(remoteStorageInfo, stream, GoogleDriveRemoteStorage.DataFileContentType);
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