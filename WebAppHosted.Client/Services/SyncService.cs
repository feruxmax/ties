using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using File = Google.Apis.Drive.v3.Data.File;

namespace WebAppHosted.Client.Services
{
    public class SyncService
    {
        private readonly DriveService _service;

        public SyncService(IAccessTokenProvider accessTokenProvider)
        {
            GoogleApiAccessTokenProvider googleApiAccessTokenProvider = new GoogleApiAccessTokenProvider(accessTokenProvider);

            _service = new DriveService(new BaseClientService.Initializer()
            {
                GZipEnabled = false, // for prevent exceptions for gzipped content
                HttpClientInitializer = googleApiAccessTokenProvider
            });
        }

        public async Task Sync()
        {
            var fileMetadata = new File()
            {
                Name = "config.json",
                Parents = new List<string>()
                {
                    "appDataFolder"
                }
            };
            var json = "{data: 2}";
            FilesResource.CreateMediaUpload createFileRequest;
            using(var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                createFileRequest = _service.Files
                    .Create(fileMetadata, stream, "application/json");
                createFileRequest.Fields = "id";
                await createFileRequest.UploadAsync();
            }

            File file = createFileRequest.ResponseBody;
            Console.WriteLine($"File ID: {file.Id}");
            await Find();
        }

        private async Task Find()
        {
            var request = _service.Files.List();
            request.Spaces = "appDataFolder";
            request.Fields = "nextPageToken, files(id, name)";
            request.PageSize = 10;
            var result = await request.ExecuteAsync();
            foreach (var file in result.Files)
            {
                Console.WriteLine($"Found file: {file.Name} ({file.Id})");
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